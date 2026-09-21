import type { IncomingMessage } from "node:http";
import type { Server } from "node:http";
import { randomBytes } from "node:crypto";
import { WebSocket, WebSocketServer } from "ws";
import type { Logger } from "pino";
import { RelayDatabase, type LinkRecord } from "./database.js";
import {
  PROTOCOL_VERSION,
  clientEnvelopeSchema,
  helloSchema,
  inboundChatSchema,
  outboundAckSchema,
  outboundChatTypes,
  pauseSchema,
  type InboundChatPayload,
  type OutboundChatPayload,
  type RelayChatType,
  type ServerEnvelope,
} from "./protocol.js";
import {
  createClientToken,
  createPairingCode,
  normalizePairingCode,
  sanitizeDiscordMessage,
  sha256,
  validateOutboundMessage,
} from "./security.js";
import { ExpiringDeduplicator, SlidingWindowRateLimiter } from "./rate-limit.js";

interface PluginSession {
  socket: WebSocket;
  remoteAddress: string;
  installationId: string | null;
  characterKey: string | null;
  characterName: string | null;
  homeWorld: string | null;
  authenticated: boolean;
  link: LinkRecord | null;
  replayGuard: ExpiringDeduplicator;
  processing: Promise<void>;
  helloReceived: boolean;
}

interface PendingPair {
  codeHash: string;
  code: string;
  installationId: string;
  expiresAt: number;
  session: PluginSession;
}

interface PendingDelivery {
  installationId: string;
  resolve: (result: DeliveryResult) => void;
  timeout: NodeJS.Timeout;
}

export type PairClaimResult =
  | { ok: true; link: LinkRecord }
  | { ok: false; error: string };

export interface DeliveryResult {
  delivered: boolean;
  error?: string;
  eventId?: string;
  link?: LinkRecord;
}

type InboundHandler = (link: LinkRecord, payload: InboundChatPayload) => Promise<void>;
type StatusHandler = (link: LinkRecord, online: boolean) => Promise<void>;

export class PluginHub {
  private readonly webSocketServer = new WebSocketServer({ noServer: true, maxPayload: 64 * 1024 });
  private readonly sessions = new Map<string, PluginSession>();
  private readonly pendingPairs = new Map<string, PendingPair>();
  private readonly pendingDeliveries = new Map<string, PendingDelivery>();
  private readonly outboundRateLimiter = new SlidingWindowRateLimiter(5, 30_000);
  private readonly pairingRateLimiter = new SlidingWindowRateLimiter(3, 10 * 60_000);
  private readonly pairClaimRateLimiter = new SlidingWindowRateLimiter(5, 10 * 60_000);
  private inboundHandler: InboundHandler | null = null;
  private statusHandler: StatusHandler | null = null;

  public constructor(
    private readonly database: RelayDatabase,
    private readonly logger: Logger,
    private readonly production: boolean,
    private readonly allowedInstallationIds: ReadonlySet<string>,
  ) {
    this.webSocketServer.on("connection", (socket, request) => this.acceptConnection(socket, request));
  }

  public attach(server: Server): void {
    server.on("upgrade", (request, socket, head) => {
      const url = new URL(request.url ?? "/", "http://relay.invalid");
      if (url.pathname !== "/v1/plugin") {
        socket.destroy();
        return;
      }
      const forwardedProtocol = String(request.headers["x-forwarded-proto"] ?? "").split(",")[0]?.trim();
      if (this.production && forwardedProtocol !== "https" && !(request.socket as { encrypted?: boolean }).encrypted) {
        socket.write("HTTP/1.1 426 Upgrade Required\r\nConnection: close\r\n\r\n");
        socket.destroy();
        return;
      }
      this.webSocketServer.handleUpgrade(request, socket, head, (webSocket) => {
        this.webSocketServer.emit("connection", webSocket, request);
      });
    });
  }

  public setInboundHandler(handler: InboundHandler): void {
    this.inboundHandler = handler;
  }

  public setStatusHandler(handler: StatusHandler): void {
    this.statusHandler = handler;
  }

  public isOnline(installationId: string): boolean {
    const session = this.sessions.get(installationId);
    return Boolean(session?.authenticated && session.socket.readyState === WebSocket.OPEN);
  }

  public claimPairingCode(codeInput: string, discordUserId: string, discordUsername: string): PairClaimResult {
    const claimRate = this.pairClaimRateLimiter.acquire(discordUserId);
    if (!claimRate.allowed) {
      return { ok: false, error: `Too many link attempts. Try again in ${Math.ceil(claimRate.retryAfterMs / 1000)} seconds.` };
    }
    this.prunePairs();
    const code = normalizePairingCode(codeInput);
    const pending = this.pendingPairs.get(sha256(code));
    if (!pending || pending.expiresAt <= Date.now()) {
      return { ok: false, error: "That link code is invalid or expired. Generate a new code in FFXIV." };
    }
    if (pending.session.socket.readyState !== WebSocket.OPEN || pending.session.installationId !== pending.installationId) {
      this.pendingPairs.delete(pending.codeHash);
      return { ok: false, error: "The FFXIV client that generated this code is no longer connected." };
    }

    const existingUserLink = this.database.findByDiscordUser(discordUserId);
    if (existingUserLink && existingUserLink.installationId !== pending.installationId) {
      return { ok: false, error: "Your Discord account is already linked to another FFXIV installation. Unlink it first." };
    }

    const token = createClientToken();
    const session = pending.session;
    const link = this.database.createLink({
      installationId: pending.installationId,
      token,
      characterKey: session.characterKey!,
      characterName: session.characterName!,
      homeWorld: session.homeWorld!,
      discordUserId,
      discordUsername,
    });
    session.authenticated = true;
    session.link = link;
    this.pendingPairs.delete(pending.codeHash);
    this.send(session, "pair.completed", {
      clientToken: token,
      discordUsername,
      relayChannelName: link.channelName,
    });
    void this.statusHandler?.(link, true).catch((error: unknown) =>
      this.logger.warn({ err: error, installationId: link.installationId }, "status delivery failed"),
    );
    return { ok: true, link };
  }

  public unlinkByDiscordUser(discordUserId: string): LinkRecord | null {
    const link = this.database.unlinkByDiscordUser(discordUserId);
    if (!link) return null;
    const session = this.sessions.get(link.installationId);
    if (session) {
      session.authenticated = false;
      session.link = null;
      this.send(session, "link.revoked", { reason: "Unlinked from Discord." });
    }
    this.rejectPendingForInstallation(link.installationId, "The character was unlinked.");
    return link;
  }

  public async sendOutbound(
    discordUserId: string,
    chatType: RelayChatType,
    messageInput: string,
  ): Promise<DeliveryResult> {
    if (!outboundChatTypes.includes(chatType as (typeof outboundChatTypes)[number])) {
      return { delivered: false, error: "That chat channel is not permitted for outbound relay." };
    }
    const validationError = validateOutboundMessage(messageInput);
    if (validationError) return { delivered: false, error: validationError };

    const link = this.database.findByDiscordUser(discordUserId);
    if (!link) return { delivered: false, error: "Your Discord account is not linked." };
    if (link.paused) return { delivered: false, error: "Relay is paused for this character.", link };

    const session = this.sessions.get(link.installationId);
    if (!session?.authenticated || session.socket.readyState !== WebSocket.OPEN) {
      return { delivered: false, error: `${link.characterName} is offline.`, link };
    }

    const rate = this.outboundRateLimiter.acquire(discordUserId);
    if (!rate.allowed) {
      return {
        delivered: false,
        error: `Outbound rate limit reached. Retry in ${Math.ceil(rate.retryAfterMs / 1000)} seconds.`,
        link,
      };
    }

    const eventId = randomBytes(16).toString("hex");
    const issuedAt = new Date();
    const payload: OutboundChatPayload = {
      eventId,
      chatType,
      message: sanitizeDiscordMessage(messageInput),
      issuedAtUtc: issuedAt.toISOString(),
      expiresAtUtc: new Date(issuedAt.getTime() + 20_000).toISOString(),
    };

    return await new Promise<DeliveryResult>((resolve) => {
      const timeout = setTimeout(() => {
        this.pendingDeliveries.delete(eventId);
        resolve({ delivered: false, error: "FFXIV did not confirm the message before timeout.", eventId, link });
      }, 9_000);
      this.pendingDeliveries.set(eventId, { installationId: link.installationId, resolve, timeout });
      this.send(session, "chat.outbound", payload);
    });
  }

  public close(): void {
    for (const session of this.sessions.values()) session.socket.close(1001, "Service shutting down");
    this.sessions.clear();
    for (const delivery of this.pendingDeliveries.values()) {
      clearTimeout(delivery.timeout);
      delivery.resolve({ delivered: false, error: "Service is shutting down." });
    }
    this.pendingDeliveries.clear();
    this.webSocketServer.close();
  }

  private acceptConnection(socket: WebSocket, request: IncomingMessage): void {
    const session: PluginSession = {
      socket,
      remoteAddress: request.socket.remoteAddress ?? "unknown",
      installationId: null,
      characterKey: null,
      characterName: null,
      homeWorld: null,
      authenticated: false,
      link: null,
      replayGuard: new ExpiringDeduplicator(5 * 60_000, 1024),
      processing: Promise.resolve(),
      helloReceived: false,
    };
    const helloTimeout = setTimeout(() => socket.close(1008, "Hello timeout"), 10_000);
    socket.on("message", (data, isBinary) => {
      if (isBinary) {
        socket.close(1003, "Text frames only");
        return;
      }
      session.processing = session.processing
        .then(() => this.handleMessage(session, data.toString("utf8")))
        .catch((error: unknown) => {
          this.logger.warn({ err: error, installationId: session.installationId }, "plugin message rejected");
          this.sendError(session, "invalid_message", "The relay message was invalid.");
        });
    });
    socket.on("close", () => {
      clearTimeout(helloTimeout);
      if (session.installationId && this.sessions.get(session.installationId) === session) {
        this.sessions.delete(session.installationId);
        this.rejectPendingForInstallation(session.installationId, "The FFXIV client disconnected.");
        if (session.link) {
          void this.statusHandler?.(session.link, false).catch((error: unknown) =>
            this.logger.warn({ err: error, installationId: session.installationId }, "offline status delivery failed"),
          );
        }
      }
    });
    socket.on("error", (error) =>
      this.logger.debug({ err: error, installationId: session.installationId }, "plugin socket error"),
    );
  }

  private async handleMessage(session: PluginSession, raw: string): Promise<void> {
    const envelope = clientEnvelopeSchema.parse(JSON.parse(raw));
    if (!session.helloReceived) {
      if (envelope.type !== "hello") {
        session.socket.close(1008, "Hello required");
        return;
      }
      this.handleHello(session, envelope.payload, envelope.requestId);
      return;
    }

    switch (envelope.type) {
      case "pair.request":
        this.handlePairRequest(session, envelope.requestId);
        return;
      case "chat.inbound":
        await this.handleInbound(session, envelope.payload);
        return;
      case "chat.outbound.ack":
        this.handleOutboundAck(session, envelope.payload);
        return;
      case "relay.pause":
        this.requireAuthenticated(session);
        this.handlePause(session, envelope.payload);
        return;
      case "link.unlink":
        this.requireAuthenticated(session);
        this.handlePluginUnlink(session);
        return;
      default:
        this.sendError(session, "unknown_type", "Unknown protocol message type.", envelope.requestId);
    }
  }

  private handleHello(session: PluginSession, payloadInput: unknown, requestId?: string | null): void {
    const payload = helloSchema.parse(payloadInput);
    if (payload.protocolVersion !== PROTOCOL_VERSION) {
      this.sendError(session, "protocol_mismatch", `Server requires protocol ${PROTOCOL_VERSION}.`, requestId);
      session.socket.close(1008, "Protocol mismatch");
      return;
    }
    if (this.allowedInstallationIds.size > 0 && !this.allowedInstallationIds.has(payload.installationId.toLowerCase())) {
      this.sendError(session, "installation_denied", "This installation is not allowed to pair.", requestId);
      session.socket.close(1008, "Installation denied");
      return;
    }

    session.helloReceived = true;
    session.installationId = payload.installationId;
    session.characterKey = payload.characterKey;
    session.characterName = payload.characterName;
    session.homeWorld = payload.homeWorld;

    const existingSession = this.sessions.get(payload.installationId);
    if (existingSession && existingSession !== session) existingSession.socket.close(4001, "Replaced by a new connection");
    this.sessions.set(payload.installationId, session);

    let link: LinkRecord | null = null;
    if (payload.clientToken) {
      link = this.database.verifyInstallation(payload.installationId, payload.clientToken);
      if (!link || link.characterKey !== payload.characterKey) {
        this.sendError(session, "authentication_failed", "Installation credential is invalid for this character.", requestId);
        session.socket.close(1008, "Authentication failed");
        return;
      }
      this.database.touchInstallation(payload.installationId, payload.characterName, payload.homeWorld, payload.paused);
      link = this.database.findByInstallation(payload.installationId)!;
      session.authenticated = true;
      session.link = link;
    }

    this.send(session, "hello.ack", {
      linked: Boolean(link),
      discordUsername: link?.discordUsername ?? null,
      relayChannelName: link?.channelName ?? null,
      backendVersion: process.env.npm_package_version ?? "0.1.0",
      serverPaused: link?.paused ?? payload.paused,
    }, requestId);
    if (link) {
      void this.statusHandler?.(link, true).catch((error: unknown) =>
        this.logger.warn({ err: error, installationId: link!.installationId }, "online status delivery failed"),
      );
    }
  }

  private handlePairRequest(session: PluginSession, requestId?: string | null): void {
    if (!session.installationId || !session.characterKey || !session.characterName || !session.homeWorld) {
      this.sendError(session, "hello_required", "Character identity is unavailable.", requestId);
      return;
    }
    if (session.authenticated) {
      this.sendError(session, "already_linked", "This installation is already linked.", requestId);
      return;
    }
    const rate = this.pairingRateLimiter.acquire(`${session.remoteAddress}:${session.installationId}`);
    if (!rate.allowed) {
      this.sendError(session, "pairing_rate_limited", `Try again in ${Math.ceil(rate.retryAfterMs / 1000)} seconds.`, requestId);
      return;
    }

    for (const [key, pair] of this.pendingPairs) {
      if (pair.installationId === session.installationId) this.pendingPairs.delete(key);
    }
    let code: string;
    let codeHash: string;
    do {
      code = createPairingCode();
      codeHash = sha256(code);
    } while (this.pendingPairs.has(codeHash));
    const expiresAt = Date.now() + 10 * 60_000;
    this.pendingPairs.set(codeHash, { codeHash, code, installationId: session.installationId, expiresAt, session });
    this.send(session, "pair.code", { code, expiresAtUtc: new Date(expiresAt).toISOString() }, requestId);
  }

  private async handleInbound(session: PluginSession, payloadInput: unknown): Promise<void> {
    this.requireAuthenticated(session);
    const payload = inboundChatSchema.parse(payloadInput);
    if (!session.replayGuard.accept(payload.eventId)) return;
    if (!session.link || session.link.paused) return;
    const age = Math.abs(Date.now() - payload.timestampUtc.getTime());
    if (age > 2 * 60_000) {
      this.sendError(session, "stale_inbound", "Stale chat message was discarded.");
      return;
    }
    if (!this.inboundHandler) throw new Error("Inbound Discord handler is unavailable.");
    await this.inboundHandler(session.link, payload);
  }

  private handleOutboundAck(session: PluginSession, payloadInput: unknown): void {
    this.requireAuthenticated(session);
    const payload = outboundAckSchema.parse(payloadInput);
    const pending = this.pendingDeliveries.get(payload.eventId);
    if (!pending || pending.installationId !== session.installationId) return;
    clearTimeout(pending.timeout);
    this.pendingDeliveries.delete(payload.eventId);
    pending.resolve({
      delivered: payload.delivered,
      ...(payload.error ? { error: payload.error } : {}),
      eventId: payload.eventId,
      ...(session.link ? { link: session.link } : {}),
    });
  }

  private handlePause(session: PluginSession, payloadInput: unknown): void {
    const payload = pauseSchema.parse(payloadInput);
    this.database.setPaused(session.installationId!, payload.paused);
    session.link = this.database.findByInstallation(session.installationId!);
  }

  private handlePluginUnlink(session: PluginSession): void {
    const installationId = session.installationId!;
    this.database.unlinkByInstallation(installationId);
    session.authenticated = false;
    session.link = null;
    this.rejectPendingForInstallation(installationId, "The character was unlinked.");
    this.send(session, "link.revoked", { reason: "Unlinked from FFXIV." });
  }

  private requireAuthenticated(session: PluginSession): void {
    if (!session.authenticated || !session.link) throw new Error("Authentication required.");
  }

  private send(session: PluginSession, type: string, payload: unknown, requestId?: string | null): void {
    if (session.socket.readyState !== WebSocket.OPEN) return;
    const envelope: ServerEnvelope = { type, payload, ...(requestId !== undefined ? { requestId } : {}) };
    session.socket.send(JSON.stringify(envelope));
  }

  private sendError(session: PluginSession, code: string, message: string, requestId?: string | null): void {
    this.send(session, "error", { code, message }, requestId);
  }

  private rejectPendingForInstallation(installationId: string, error: string): void {
    for (const [eventId, pending] of this.pendingDeliveries) {
      if (pending.installationId !== installationId) continue;
      clearTimeout(pending.timeout);
      this.pendingDeliveries.delete(eventId);
      pending.resolve({ delivered: false, error, eventId });
    }
  }

  private prunePairs(now = Date.now()): void {
    for (const [key, pending] of this.pendingPairs) {
      if (pending.expiresAt <= now || pending.session.socket.readyState !== WebSocket.OPEN) this.pendingPairs.delete(key);
    }
  }
}
