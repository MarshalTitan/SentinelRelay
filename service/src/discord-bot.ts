import {
  ChannelType,
  Client,
  EmbedBuilder,
  GatewayIntentBits,
  MessageFlags,
  PermissionFlagsBits,
  REST,
  Routes,
  SlashCommandBuilder,
  type ChatInputCommandInteraction,
  type ColorResolvable,
} from "discord.js";
import type { Logger } from "pino";
import { RelayDatabase, type LinkRecord } from "./database.js";
import { PluginHub } from "./plugin-hub.js";
import type { InboundChatPayload, RelayChatType } from "./protocol.js";
import { ExpiringDeduplicator } from "./rate-limit.js";
import { sha256 } from "./security.js";

const channelColors: Partial<Record<RelayChatType, ColorResolvable>> = {
  Say: 0xe7e7e7,
  Yell: 0xf2d447,
  Shout: 0xf06a42,
  FreeCompany: 0x48b8ef,
  Party: 0x4f8ff7,
  Alliance: 0xff8b42,
  Tell: 0xf16cb5,
  PvPTeam: 0xe5a55e,
};

const simpleOutboundCommands: ReadonlyArray<[string, string, RelayChatType]> = [
  ["fc", "Send a message to FFXIV Free Company chat.", "FreeCompany"],
  ["say", "Send a message to FFXIV Say chat.", "Say"],
  ["yell", "Send a message to FFXIV Yell chat.", "Yell"],
  ["shout", "Send a message to FFXIV Shout chat.", "Shout"],
  ["party", "Send a message to FFXIV Party chat.", "Party"],
  ["alliance", "Send a message to FFXIV Alliance chat.", "Alliance"],
  ["pvpteam", "Send a message to FFXIV PvP Team chat.", "PvPTeam"],
];

export class DiscordRelayBot {
  private readonly client = new Client({ intents: [GatewayIntentBits.Guilds] });
  private readonly alertDeduplicator = new ExpiringDeduplicator(60_000);
  private readonly statusByInstallation = new Map<string, boolean>();
  private readonly offlineTimers = new Map<string, NodeJS.Timeout>();

  public constructor(
    private readonly database: RelayDatabase,
    private readonly hub: PluginHub,
    private readonly logger: Logger,
    private readonly token: string,
    private readonly clientId: string,
    private readonly guildId?: string,
  ) {
    this.client.on("interactionCreate", (interaction) => {
      if (!interaction.isChatInputCommand()) return;
      void this.handleInteraction(interaction).catch((error: unknown) => {
        this.logger.error({ err: error, command: interaction.commandName, userId: interaction.user.id }, "Discord command failed");
        const message = { content: "Sentinel Relay could not complete that command.", flags: MessageFlags.Ephemeral } as const;
        if (interaction.replied || interaction.deferred) void interaction.followUp(message);
        else void interaction.reply(message);
      });
    });
    this.client.once("ready", (client) =>
      this.logger.info({ botUser: client.user.tag }, "Sentinel Relay Discord bot connected"),
    );
  }

  public async start(): Promise<void> {
    await this.registerCommands();
    await this.client.login(this.token);
  }

  public isReady(): boolean {
    return this.client.isReady();
  }

  public async stop(): Promise<void> {
    for (const timer of this.offlineTimers.values()) clearTimeout(timer);
    this.offlineTimers.clear();
    this.client.destroy();
  }

  public async deliverInbound(link: LinkRecord, payload: InboundChatPayload): Promise<void> {
    if (!link.channelId) throw new Error("No Discord relay channel is configured.");
    const channel = await this.client.channels.fetch(link.channelId);
    if (!channel?.isSendable()) throw new Error("Configured Discord relay channel is unavailable or not sendable.");

    const wantsMentionAlert = payload.keywordMatches.some(
      (match) => match.alertMethod === "ChannelMention" || match.alertMethod === "Both",
    );
    const wantsDmAlert = payload.keywordMatches.some(
      (match) => match.alertMethod === "DirectMessage" || match.alertMethod === "Both",
    );
    const alertSignature = sha256(`${payload.chatType}\0${payload.sender}\0${payload.message}`);
    const alertAllowed = (!wantsMentionAlert && !wantsDmAlert)
      || this.alertDeduplicator.accept(`${link.installationId}:${alertSignature}`);
    const mentionAlert = wantsMentionAlert && alertAllowed;
    const dmAlert = wantsDmAlert && alertAllowed;
    const embed = this.chatEmbed(link, payload);
    await channel.send({
      ...(mentionAlert ? { content: `<@${link.discordUserId}>` } : {}),
      embeds: [embed],
      allowedMentions: { users: mentionAlert ? [link.discordUserId] : [] },
    });

    if (dmAlert) {
      const user = await this.client.users.fetch(link.discordUserId);
      const keywords = [...new Set(payload.keywordMatches.map((match) => match.keyword))];
      await user.send({
        content: `Keyword alert: ${keywords.map((value) => `“${value}”`).join(", ")}`,
        embeds: [embed],
      }).catch((error: unknown) =>
        this.logger.warn({ err: error, installationId: link.installationId, userId: link.discordUserId }, "keyword DM failed"),
      );
    }
  }

  public async handleStatus(link: LinkRecord, online: boolean): Promise<void> {
    const existingTimer = this.offlineTimers.get(link.installationId);
    if (existingTimer) {
      clearTimeout(existingTimer);
      this.offlineTimers.delete(link.installationId);
    }
    if (online) {
      if (this.statusByInstallation.get(link.installationId) === true) return;
      this.statusByInstallation.set(link.installationId, true);
      await this.sendStatus(link, true);
      return;
    }

    const timer = setTimeout(() => {
      this.offlineTimers.delete(link.installationId);
      if (this.hub.isOnline(link.installationId)) return;
      if (this.statusByInstallation.get(link.installationId) === false) return;
      this.statusByInstallation.set(link.installationId, false);
      void this.sendStatus(link, false);
    }, 30_000);
    this.offlineTimers.set(link.installationId, timer);
  }

  public async sendOutboundEcho(link: LinkRecord, chatType: RelayChatType, message: string): Promise<void> {
    if (!link.channelId) return;
    const channel = await this.client.channels.fetch(link.channelId);
    if (!channel?.isSendable()) return;
    await channel.send({
      embeds: [new EmbedBuilder()
        .setColor(channelColors[chatType] ?? 0x35c9f2)
        .setTitle(`[${shortLabel(chatType)}] ${link.characterName} @ ${link.homeWorld}`)
        .setDescription(message)
        .setFooter({ text: "Sent from Discord through Sentinel Relay" })
        .setTimestamp()],
    });
  }

  private async registerCommands(): Promise<void> {
    const commands = buildCommands().map((command) => command.toJSON());
    const rest = new REST({ version: "10" }).setToken(this.token);
    const route = this.guildId
      ? Routes.applicationGuildCommands(this.clientId, this.guildId)
      : Routes.applicationCommands(this.clientId);
    await rest.put(route, { body: commands });
    this.logger.info({ commandCount: commands.length, scope: this.guildId ? "guild" : "global" }, "Discord commands registered");
  }

  private async handleInteraction(interaction: ChatInputCommandInteraction): Promise<void> {
    if (interaction.commandName === "relay") {
      await this.handleRelayCommand(interaction);
      return;
    }

    const simple = simpleOutboundCommands.find(([name]) => name === interaction.commandName);
    if (simple) {
      await this.handleOutbound(interaction, simple[2], interaction.options.getString("message", true));
      return;
    }
    if (interaction.commandName === "ls" || interaction.commandName === "cwls") {
      const number = interaction.options.getInteger("channel", true);
      const prefix = interaction.commandName === "ls" ? "Linkshell" : "CrossWorldLinkshell";
      await this.handleOutbound(
        interaction,
        `${prefix}${number}` as RelayChatType,
        interaction.options.getString("message", true),
      );
    }
  }

  private async handleRelayCommand(interaction: ChatInputCommandInteraction): Promise<void> {
    const subcommand = interaction.options.getSubcommand();
    if (subcommand === "link") {
      const result = this.hub.claimPairingCode(
        interaction.options.getString("code", true),
        interaction.user.id,
        interaction.user.globalName ?? interaction.user.username,
      );
      await interaction.reply({
        content: result.ok
          ? `Linked to **${result.link.characterName} @ ${result.link.homeWorld}**. Next, run \`/relay channel\` in this server.`
          : result.error,
        flags: MessageFlags.Ephemeral,
      });
      return;
    }

    const link = this.database.findByDiscordUser(interaction.user.id);
    if (subcommand === "status") {
      await interaction.reply({
        content: link
          ? `**${link.characterName} @ ${link.homeWorld}** — ${this.hub.isOnline(link.installationId) ? "ONLINE" : "OFFLINE"}; ${link.paused ? "PAUSED" : "running"}; destination ${link.channelName ? `#${link.channelName}` : "not configured"}.`
          : "Your Discord account is not linked.",
        flags: MessageFlags.Ephemeral,
      });
      return;
    }
    if (subcommand === "unlink") {
      const removed = this.hub.unlinkByDiscordUser(interaction.user.id);
      await interaction.reply({
        content: removed ? `Unlinked **${removed.characterName}**.` : "Your Discord account is not linked.",
        flags: MessageFlags.Ephemeral,
      });
      return;
    }
    if (subcommand === "channel") {
      if (!link) {
        await interaction.reply({ content: "Link your FFXIV character first.", flags: MessageFlags.Ephemeral });
        return;
      }
      const selectedOption = interaction.options.getChannel("channel", true);
      const guild = interaction.guild;
      const guildId = interaction.guildId;
      const selected = guild ? await guild.channels.fetch(selectedOption.id) : null;
      if (!guildId || !selected || selected.type !== ChannelType.GuildText || selected.guildId !== guildId) {
        await interaction.reply({ content: "Choose a normal text channel in this server.", flags: MessageFlags.Ephemeral });
        return;
      }
      const userPermissions = selected.permissionsFor(interaction.user);
      const botMember = selected.guild.members.me ?? await selected.guild.members.fetchMe();
      const botPermissions = selected.permissionsFor(botMember);
      const required = [PermissionFlagsBits.ViewChannel, PermissionFlagsBits.SendMessages, PermissionFlagsBits.EmbedLinks];
      if (!userPermissions?.has([PermissionFlagsBits.ViewChannel, PermissionFlagsBits.SendMessages])) {
        await interaction.reply({ content: "You need View Channel and Send Messages in that channel.", flags: MessageFlags.Ephemeral });
        return;
      }
      if (!botPermissions?.has(required)) {
        await interaction.reply({ content: "Sentinel Relay needs View Channel, Send Messages, and Embed Links there.", flags: MessageFlags.Ephemeral });
        return;
      }
      const updated = this.database.setChannel(link.installationId, guildId, selected.id, selected.name);
      await interaction.reply({ content: `Relay destination set to ${selected}.`, flags: MessageFlags.Ephemeral });
      await this.sendStatus(updated, this.hub.isOnline(updated.installationId));
    }
  }

  private async handleOutbound(
    interaction: ChatInputCommandInteraction,
    chatType: RelayChatType,
    message: string,
  ): Promise<void> {
    const link = this.database.findByDiscordUser(interaction.user.id);
    if (!link) {
      await interaction.reply({ content: "Your Discord account is not linked.", flags: MessageFlags.Ephemeral });
      return;
    }
    if (!link.channelId || interaction.channelId !== link.channelId || interaction.guildId !== link.guildId) {
      await interaction.reply({
        content: `Use this command in ${link.channelId ? `<#${link.channelId}>` : "your configured relay channel"}.`,
        flags: MessageFlags.Ephemeral,
      });
      return;
    }

    await interaction.deferReply({ flags: MessageFlags.Ephemeral });
    const result = await this.hub.sendOutbound(interaction.user.id, chatType, message);
    if (!result.delivered || !result.link) {
      await interaction.editReply(result.error ?? "FFXIV did not confirm the message.");
      return;
    }
    await this.sendOutboundEcho(result.link, chatType, message);
    await interaction.editReply(`Sent to ${shortLabel(chatType)} as ${result.link.characterName}.`);
  }

  private chatEmbed(link: LinkRecord, payload: InboundChatPayload): EmbedBuilder {
    const world = payload.senderWorld ? ` @ ${payload.senderWorld}` : "";
    return new EmbedBuilder()
      .setColor(channelColors[payload.chatType] ?? 0x35c9f2)
      .setTitle(`[${payload.channelLabel}] ${payload.sender}${world}`)
      .setDescription(payload.message)
      .setFooter({ text: link.characterName })
      .setTimestamp(payload.timestampUtc);
  }

  private async sendStatus(link: LinkRecord, online: boolean): Promise<void> {
    if (!link.channelId) return;
    const channel = await this.client.channels.fetch(link.channelId).catch(() => null);
    if (!channel?.isSendable()) return;
    await channel.send({
      embeds: [new EmbedBuilder()
        .setColor(online ? 0x42d978 : 0x78808c)
        .setTitle(`Sentinel Relay — ${link.characterName} ${online ? "ONLINE" : "OFFLINE"}`)
        .setDescription(online ? "Selected FFXIV chat channels are connected." : "Discord messages cannot be sent through this character right now.")
        .setTimestamp()],
    });
  }
}

function buildCommands() {
  const relay = new SlashCommandBuilder()
    .setName("relay")
    .setDescription("Manage your Sentinel Relay link.")
    .addSubcommand((command) => command
      .setName("link")
      .setDescription("Link this Discord account using a one-time FFXIV code.")
      .addStringOption((option) => option.setName("code").setDescription("Code shown by /srelay link").setRequired(true).setMaxLength(12)))
    .addSubcommand((command) => command.setName("status").setDescription("Show your linked character and connection state."))
    .addSubcommand((command) => command.setName("unlink").setDescription("Revoke and remove your character link."))
    .addSubcommand((command) => command
      .setName("channel")
      .setDescription("Set the one Discord relay channel for your character.")
      .addChannelOption((option) => option
        .setName("channel")
        .setDescription("Private text channel used for this character")
        .addChannelTypes(ChannelType.GuildText)
        .setRequired(true)));

  const simple = simpleOutboundCommands.map(([name, description]) => new SlashCommandBuilder()
    .setName(name)
    .setDescription(description)
    .addStringOption((option) => option
      .setName("message")
      .setDescription("Text to send through your linked FFXIV character")
      .setRequired(true)
      .setMaxLength(180)));

  const numbered = ["ls", "cwls"].map((name) => new SlashCommandBuilder()
    .setName(name)
    .setDescription(`Send a message to FFXIV ${name === "ls" ? "Linkshell" : "Cross-world Linkshell"} chat.`)
    .addIntegerOption((option) => option.setName("channel").setDescription("Channel number 1–8").setRequired(true).setMinValue(1).setMaxValue(8))
    .addStringOption((option) => option.setName("message").setDescription("Text to send through FFXIV").setRequired(true).setMaxLength(180)));

  return [relay, ...simple, ...numbered];
}

function shortLabel(chatType: RelayChatType): string {
  if (chatType === "FreeCompany") return "FC";
  if (chatType === "PvPTeam") return "PVP TEAM";
  if (chatType.startsWith("CrossWorldLinkshell")) return chatType.replace("CrossWorldLinkshell", "CWLS ");
  if (chatType.startsWith("Linkshell")) return chatType.replace("Linkshell", "LS ");
  return chatType.toUpperCase();
}
