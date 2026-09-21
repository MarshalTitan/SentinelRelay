import { z } from "zod";

export const PROTOCOL_VERSION = 1;

export const relayChatTypes = [
  "Say", "Yell", "Shout", "Tell", "Party", "Alliance", "FreeCompany", "PvPTeam",
  "Linkshell1", "Linkshell2", "Linkshell3", "Linkshell4", "Linkshell5", "Linkshell6", "Linkshell7", "Linkshell8",
  "CrossWorldLinkshell1", "CrossWorldLinkshell2", "CrossWorldLinkshell3", "CrossWorldLinkshell4",
  "CrossWorldLinkshell5", "CrossWorldLinkshell6", "CrossWorldLinkshell7", "CrossWorldLinkshell8",
  "NoviceNetwork", "StandardEmote", "CustomEmote",
] as const;

export type RelayChatType = (typeof relayChatTypes)[number];

export const outboundChatTypes = relayChatTypes.filter(
  (type) => !["Tell", "NoviceNetwork", "StandardEmote", "CustomEmote"].includes(type),
) as Exclude<RelayChatType, "Tell" | "NoviceNetwork" | "StandardEmote" | "CustomEmote">[];

const isoDate = z.string().datetime({ offset: true }).transform((value) => new Date(value));

export const helloSchema = z.object({
  installationId: z.string().uuid(),
  clientToken: z.string().min(32).max(256).nullable().optional(),
  protocolVersion: z.number().int(),
  pluginVersion: z.string().min(1).max(32),
  characterName: z.string().min(3).max(64),
  homeWorld: z.string().min(1).max(64),
  characterKey: z.string().min(4).max(128),
  paused: z.boolean(),
});

export const keywordMatchSchema = z.object({
  keyword: z.string().min(1).max(80),
  alertMethod: z.enum(["DirectMessage", "ChannelMention", "Both"]),
});

export const inboundChatSchema = z.object({
  eventId: z.string().regex(/^[a-f0-9]{32}$/i),
  chatType: z.enum(relayChatTypes),
  channelLabel: z.string().min(1).max(16),
  sender: z.string().min(1).max(96),
  senderWorld: z.string().max(64).nullable().optional(),
  message: z.string().min(1).max(400),
  timestampUtc: isoDate,
  keywordMatches: z.array(keywordMatchSchema).max(20),
});

export const outboundAckSchema = z.object({
  eventId: z.string().regex(/^[a-f0-9]{32}$/i),
  delivered: z.boolean(),
  error: z.string().max(300).nullable().optional(),
});

export const pauseSchema = z.object({ paused: z.boolean() });

export const clientEnvelopeSchema = z.object({
  type: z.string().min(1).max(64),
  requestId: z.string().max(64).nullable().optional(),
  payload: z.unknown(),
});

export type HelloPayload = z.infer<typeof helloSchema>;
export type InboundChatPayload = z.infer<typeof inboundChatSchema>;
export type OutboundAckPayload = z.infer<typeof outboundAckSchema>;

export interface OutboundChatPayload {
  eventId: string;
  chatType: RelayChatType;
  message: string;
  issuedAtUtc: string;
  expiresAtUtc: string;
}

export interface ServerEnvelope {
  type: string;
  requestId?: string | null;
  payload: unknown;
}

