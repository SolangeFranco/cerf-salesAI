import { createServer } from "node:http";
import { mkdir, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { join } from "node:path";
import makeWASocket, { Browsers, DisconnectReason, downloadMediaMessage, fetchLatestBaileysVersion, useMultiFileAuthState } from "@whiskeysockets/baileys";
import pino from "pino";
import QRCode from "qrcode";

const port = Number(process.env.PORT ?? 5080);
const bridgeKey = process.env.BRIDGE_KEY ?? "";
const webhookSecret = process.env.LINKED_WEBHOOK_SECRET ?? "";
const webhookUrl = process.env.LINKED_WEBHOOK_URL ?? "";
const sessionsRoot = process.env.SESSIONS_DIR ?? join(process.cwd(), ".data", "sessions");
const sessions = new Map();
const maxMediaBytes = 10 * 1024 * 1024;
let baileysVersion;

try {
  ({ version: baileysVersion } = await fetchLatestBaileysVersion());
  console.log(`Using current Baileys version ${baileysVersion.join(".")}`);
} catch (error) {
  console.warn("Could not fetch current Baileys version; using package defaults", error);
}

await mkdir(sessionsRoot, { recursive: true });

for (const entry of await readdir(sessionsRoot, { withFileTypes: true })) {
  if (!entry.isDirectory()) continue;
  try {
    let metadata = {};
    try {
      metadata = JSON.parse(await readFile(join(sessionsRoot, entry.name, "metadata.json"), "utf8"));
    } catch {
      console.warn(`Restoring legacy WhatsApp session ${entry.name} without metadata`);
    }
    const session = { id: entry.name, status: "starting", qr: null, webhookUrl: webhookUrl || metadata.webhookUrl, webhookSecret, socket: null, removing: false, contacts: new Map(), contactImages: new Map(), profileImageLookups: new Map(), lidToPhone: new Map(), chats: new Map() };
    sessions.set(session.id, session);
    await connectSession(session);
  } catch (error) {
    console.error(`Could not restore WhatsApp session ${entry.name}`, error);
  }
}

function authorized(request) {
  return !bridgeKey || request.headers["x-bridge-key"] === bridgeKey;
}

function json(response, status, body) {
  response.writeHead(status, { "content-type": "application/json; charset=utf-8" });
  response.end(JSON.stringify(body));
}

async function readBody(request) {
  let body = "";
  for await (const chunk of request) body += chunk;
  return body ? JSON.parse(body) : {};
}

async function postWebhook(session, payload) {
  if (!session.webhookUrl) return;
  for (let attempt = 0; attempt < 5; attempt++) {
    try {
      const response = await fetch(session.webhookUrl, {
        method: "POST",
        headers: { "content-type": "application/json", "authorization": `Bridge ${bridgeKey}`, "x-linked-device-signature": session.webhookSecret, "x-bridge-key": bridgeKey },
        body: JSON.stringify({ ...payload, sessionId: session.id }),
      });
      if (response.ok) return;
      const detail = await response.text();
      if (response.status !== 429) {
        console.error(`Webhook rejected for ${session.id}: ${response.status} ${detail}`);
        return;
      }
      await new Promise(resolve => setTimeout(resolve, 250 * (attempt + 1)));
    } catch (error) {
      console.error(`Webhook request failed for ${session.id}`, error);
      await new Promise(resolve => setTimeout(resolve, 250 * (attempt + 1)));
    }
  }
  console.error(`Webhook retries exhausted for ${session.id}`);
}

function messageText(message) {
  return message.message?.conversation ?? message.message?.extendedTextMessage?.text ?? message.message?.imageMessage?.caption ?? message.message?.videoMessage?.caption ?? message.message?.documentMessage?.caption ?? "";
}

function quotedMessage(message) {
  const context = message.message?.extendedTextMessage?.contextInfo ?? message.message?.imageMessage?.contextInfo ?? message.message?.videoMessage?.contextInfo ?? message.message?.documentMessage?.contextInfo;
  return context?.stanzaId ? { id: context.stanzaId, text: context.quotedMessage?.conversation ?? context.quotedMessage?.extendedTextMessage?.text ?? context.quotedMessage?.imageMessage?.caption ?? "" } : null;
}

function isPhoneJid(jid) {
  return Boolean(jid) && (jid.endsWith("@s.whatsapp.net") || jid.endsWith("@c.us"));
}

// WhatsApp hides the display name for phone-number-privacy contacts and reports a masked
// string like "+595∙∙∙∙∙∙∙96" as name/notify/pushName instead of the real saved name.
function isMaskedContactName(name) {
  if (typeof name !== "string") return false;
  const trimmed = name.trim();
  if (!trimmed) return false;
  return /[∙•●·]/.test(trimmed) && /^[+\d\s\-∙•●·]+$/.test(trimmed);
}

function resolveDisplayName(...candidates) {
  for (const candidate of candidates) {
    if (candidate && !isMaskedContactName(candidate)) return candidate;
  }
  return null;
}

function registerLidMapping(session, lid, phone) {
  if (!lid || !phone || !lid.endsWith("@lid") || !isPhoneJid(phone)) return;
  session.lidToPhone.set(lid, phone);
}

function forwardIdentityMapping(session, lid, phone, contactName = null, contactImageUrl = null) {
  if (!lid || !phone || !lid.endsWith("@lid") || !isPhoneJid(phone)) return;
  void postWebhook(session, {
    type: "identity",
    eventId: `identity:${lid}:${phone}`,
    sourceId: externalId(lid),
    contactId: externalId(phone),
    contactName,
    contactImageUrl,
  });
}

function canonicalContactJid(session, primaryJid, alternateJid) {
  if (isPhoneJid(alternateJid)) return alternateJid;
  if (isPhoneJid(primaryJid)) return primaryJid;
  return session.lidToPhone.get(primaryJid) ?? session.lidToPhone.get(alternateJid) ?? primaryJid;
}

function externalId(jid) {
  return jid.replace(/@s\.whatsapp\.net|@c\.us|@g\.us|@lid|@newsletter/g, "");
}

function isReactionMessage(message) {
  return Boolean(message.message?.reactionMessage);
}

function mediaDetails(message) {
  const content = message.message;
  if (content?.imageMessage) return { kind: "image", mimeType: content.imageMessage.mimetype ?? "image/jpeg", fileName: null };
  if (content?.videoMessage) return { kind: "video", mimeType: content.videoMessage.mimetype ?? "video/mp4", fileName: null };
  if (content?.audioMessage) return { kind: "audio", mimeType: content.audioMessage.mimetype ?? "audio/ogg", fileName: null };
  if (content?.documentMessage) return { kind: "document", mimeType: content.documentMessage.mimetype ?? "application/octet-stream", fileName: content.documentMessage.fileName ?? null };
  if (content?.stickerMessage) return { kind: "sticker", mimeType: content.stickerMessage.mimetype ?? "image/webp", fileName: null };
  return null;
}

async function downloadAttachment(session, message, media) {
  try {
    const data = await downloadMediaMessage(message, "buffer", {}, { logger: pino({ level: "silent" }), reuploadRequest: current => session.socket.updateMediaMessage(current) });
    if (data.length > maxMediaBytes) return { ...media, unavailable: true };
    return { ...media, dataBase64: data.toString("base64"), unavailable: false };
  } catch (error) {
    console.warn(`Could not download ${media.kind} attachment for ${message.key?.id ?? "unknown"}`, error);
    return { ...media, unavailable: true };
  }
}

async function profileImageUrl(session, jid) {
  if (session.contactImages.has(jid)) return session.contactImages.get(jid);
  let lookup = session.profileImageLookups.get(jid);
  if (!lookup) {
    lookup = session.socket?.profilePictureUrl(jid, "image").catch(() => null) ?? Promise.resolve(null);
    session.profileImageLookups.set(jid, lookup);
  }
  const imageUrl = await lookup;
  if (imageUrl) session.contactImages.set(jid, imageUrl);
  return imageUrl;
}

async function forwardMessage(session, message, historical = false) {
  const remoteJid = message.key?.remoteJid;
  const alternateJid = message.key?.remoteJidAlt;
  const messageId = message.key?.id;
  const reaction = message.message?.reactionMessage;
  const text = messageText(message);
  const media = mediaDetails(message);
  const quoted = quotedMessage(message);
  if (!remoteJid || remoteJid.endsWith("@broadcast") || !messageId) return;
  if (reaction) {
    await forwardReaction(session, message.key, reaction);
    return;
  }
  if (!text.trim() && !media) return;
  const contactJid = canonicalContactJid(session, remoteJid, alternateJid);
  const timestampSeconds = Number(message.messageTimestamp?.toString?.() ?? message.messageTimestamp ?? 0);
  const attachment = media ? await downloadAttachment(session, message, media) : null;
  await postWebhook(session, {
    type: "message",
    eventId: `${historical ? "history" : "message"}:${messageId}`,
    messageId,
    from: externalId(contactJid),
    // Prefer the name WE saved for this contact (address book) over the name the WhatsApp
    // user chose for themselves (pushName); only fall back to pushName if we saved none.
    contactName: resolveDisplayName(session.contacts.get(contactJid), session.contacts.get(remoteJid), session.chats.get(remoteJid), message.pushName),
    contactImageUrl: await profileImageUrl(session, contactJid),
    createdAt: timestampSeconds > 0 ? new Date(timestampSeconds * 1000).toISOString() : new Date().toISOString(),
    isGroup: remoteJid.endsWith("@g.us"),
    isChannel: remoteJid.endsWith("@newsletter"),
    text,
    quotedMessageId: quoted?.id ?? null,
    quotedText: quoted?.text ?? null,
    mediaBase64: attachment?.dataBase64 ?? null,
    mediaMimeType: attachment?.mimeType ?? null,
    mediaFileName: attachment?.fileName ?? null,
    mediaKind: attachment?.kind ?? null,
    mediaUnavailable: attachment?.unavailable ?? false,
    fromMe: Boolean(message.key.fromMe),
    historical,
  });
}

async function forwardChat(session, chat) {
  const remoteJid = chat.id;
  if (!remoteJid || remoteJid.endsWith("@broadcast")) return;
  const isGroup = remoteJid.endsWith("@g.us");
  const isChannel = remoteJid.endsWith("@newsletter");
  if (!isGroup && !isChannel) return;
  const contactJid = chat.id;
  const timestampSeconds = Number(chat.conversationTimestamp?.toString?.() ?? chat.conversationTimestamp ?? 0);
  const contactName = resolveDisplayName(session.contacts.get(contactJid), chat.name, session.chats.get(remoteJid));
  const contactImageUrl = await profileImageUrl(session, contactJid);
  await postWebhook(session, {
    type: "chat",
    eventId: `chat:${remoteJid}:${timestampSeconds}:${contactName ?? ""}`,
    contactId: contactJid.replace(/@s\.whatsapp\.net|@g\.us|@lid|@newsletter/g, ""),
    contactName,
    contactImageUrl,
    createdAt: timestampSeconds > 0 ? new Date(timestampSeconds * 1000).toISOString() : new Date().toISOString(),
    isGroup,
    isChannel,
  });
}

async function forwardReaction(session, key, reaction) {
  const targetMessageId = reaction?.key?.id;
  const emoji = reaction?.text;
  const remoteJid = reaction?.key?.remoteJid ?? key?.remoteJid;
  if (!targetMessageId || !remoteJid || remoteJid.endsWith("@broadcast")) return;
  const sender = canonicalContactJid(session, key?.participant ?? key?.remoteJid ?? "unknown", key?.participantAlt);
  await postWebhook(session, {
    type: "reaction",
    eventId: `reaction:${targetMessageId}:${sender}:${emoji ?? "removed"}`,
    messageId: targetMessageId,
    emoji: emoji ?? "",
    sender: externalId(sender),
  });
}

async function connectSession(session) {
  const { state, saveCreds } = await useMultiFileAuthState(join(sessionsRoot, session.id));
  const socket = makeWASocket({ auth: state, browser: Browsers.macOS("Chrome"), version: baileysVersion, syncFullHistory: true, markOnlineOnConnect: false, printQRInTerminal: false, qrTimeout: 60000, connectTimeoutMs: 30000, logger: pino({ level: "silent" }) });
  session.socket = socket;
  session.status = "connecting";

  socket.ev.on("creds.update", saveCreds);
  socket.ev.on("contacts.upsert", contacts => {
    for (const contact of contacts) {
      const name = resolveDisplayName(contact.name, contact.notify);
      registerLidMapping(session, contact.lid, contact.phoneNumber);
      forwardIdentityMapping(session, contact.lid, contact.phoneNumber, name, contact.imgUrl ?? null);
      const contactJid = canonicalContactJid(session, contact.phoneNumber ?? contact.id, contact.id);
      if (contactJid && contact.imgUrl) {
        session.contactImages.set(contactJid, contact.imgUrl);
        session.contactImages.set(contact.id, contact.imgUrl);
        session.profileImageLookups.delete(contactJid);
      }
      if (contact.id && name) {
        session.contacts.set(contact.id, name);
        session.contacts.set(contactJid, name);
        void postWebhook(session, {
          type: "contact",
          eventId: `contact:${contactJid}:${name}:${contact.imgUrl ?? ""}`,
          contactId: externalId(contactJid),
          contactName: name,
          contactImageUrl: contact.imgUrl ?? null,
        });
      }
    }
  });
  socket.ev.on("chats.upsert", async chats => {
    for (const chat of chats) {
      if (chat.id && chat.name) session.chats.set(chat.id, chat.name);
      await forwardChat(session, chat);
    }
  });
  socket.ev.on("connection.update", async ({ connection, lastDisconnect, qr }) => {
    if (qr) {
      session.status = "awaiting_qr";
      session.qr = await QRCode.toDataURL(qr);
      console.log(`WhatsApp session ${session.id} emitted a QR code`);
    }
    if (connection === "open") {
      session.status = "connected";
      session.qr = null;
      console.log(`WhatsApp session ${session.id} connected`);
    }
    if (connection === "close") {
      session.socket = null;
      if (session.removing) return;
      const code = lastDisconnect?.error?.output?.statusCode;
      session.status = code === DisconnectReason.loggedOut ? "logged_out" : "disconnected";
      session.qr = null;
      if (code !== DisconnectReason.loggedOut) setTimeout(() => connectSession(session), 1000);
      console.warn(`WhatsApp session ${session.id} closed with code ${code ?? "unknown"}: ${lastDisconnect?.error?.message ?? "unknown error"}`);
    }
  });

  socket.ev.on("messages.upsert", async ({ messages, type }) => {
    if (type !== "notify") return;
    for (const message of messages) await forwardMessage(session, message);
  });

  socket.ev.on("messaging-history.set", async ({ chats, contacts, lidPnMappings, messages }) => {
    for (const mapping of lidPnMappings ?? []) {
      registerLidMapping(session, mapping.lid, mapping.pn);
      forwardIdentityMapping(session, mapping.lid, mapping.pn);
    }
    for (const contact of contacts ?? []) {
      registerLidMapping(session, contact.lid, contact.phoneNumber);
      const name = resolveDisplayName(contact.name, contact.notify);
      forwardIdentityMapping(session, contact.lid, contact.phoneNumber, name, contact.imgUrl ?? null);
      const contactJid = canonicalContactJid(session, contact.phoneNumber ?? contact.id, contact.id);
      if (name) {
        session.contacts.set(contact.id, name);
        session.contacts.set(contactJid, name);
      }
      if (contact.imgUrl) {
        session.contactImages.set(contact.id, contact.imgUrl);
        session.contactImages.set(contactJid, contact.imgUrl);
      }
    }
    for (const chat of chats ?? []) {
      if (chat.id && chat.name) session.chats.set(chat.id, chat.name);
      await forwardChat(session, chat);
    }
    console.log(`WhatsApp session ${session.id} history received: ${messages.length} messages`);
    for (const message of messages.filter(message => !isReactionMessage(message))) await forwardMessage(session, message, true);
    for (const message of messages.filter(isReactionMessage)) await forwardMessage(session, message, true);
  });

  socket.ev.on("lid-mapping.update", mapping => {
    registerLidMapping(session, mapping.lid, mapping.pn);
    forwardIdentityMapping(session, mapping.lid, mapping.pn);
  });

  socket.ev.on("messages.reaction", async reactions => {
    for (const { key, reaction } of reactions) await forwardReaction(session, key, reaction);
  });
}

async function createSession(input) {
  const id = crypto.randomUUID();
  const session = { id, status: "starting", qr: null, webhookUrl: input.webhookUrl, webhookSecret: webhookSecret || input.webhookSecret, socket: null, removing: false, contacts: new Map(), contactImages: new Map(), profileImageLookups: new Map(), lidToPhone: new Map(), chats: new Map() };
  sessions.set(id, session);
  await mkdir(join(sessionsRoot, id), { recursive: true });
  await writeFile(join(sessionsRoot, id, "metadata.json"), JSON.stringify({ webhookUrl: session.webhookUrl }));
  await connectSession(session);
  return session;
}

async function removeSession(session) {
  session.removing = true;
  session.qr = null;
  try {
    await session.socket?.logout();
  } catch (error) {
    console.warn(`Could not log out WhatsApp session ${session.id}`, error);
  } finally {
    session.socket = null;
    sessions.delete(session.id);
    try {
      await rm(join(sessionsRoot, session.id), { recursive: true, force: true });
    } catch (error) {
      console.error(`Could not remove WhatsApp session files ${session.id}`, error);
    }
  }
}

const server = createServer(async (request, response) => {
  try {
    if (!authorized(request)) return json(response, 401, { error: "unauthorized" });
    const url = new URL(request.url, `http://${request.headers.host}`);
    const parts = url.pathname.split("/").filter(Boolean);

    if (request.method === "POST" && url.pathname === "/sessions") {
      const session = await createSession(await readBody(request));
      return json(response, 201, { sessionId: session.id, status: session.status, qr: session.qr });
    }

    if (parts[0] === "sessions" && parts[1]) {
      const session = sessions.get(parts[1]);
      if (!session && request.method === "DELETE" && parts.length === 2) {
        response.writeHead(204);
        return response.end();
      }
      if (!session) return json(response, 404, { error: "session_not_found" });

      if (request.method === "GET" && parts.length === 2)
        return json(response, 200, { sessionId: session.id, status: session.status, qr: session.qr });

      if (request.method === "DELETE" && parts.length === 2) {
        await removeSession(session);
        response.writeHead(204);
        return response.end();
      }

      if (request.method === "POST" && parts[2] === "messages") {
        if (session.status !== "connected" || !session.socket) return json(response, 409, { error: "session_not_connected" });
        const input = await readBody(request);
        const jid = input.to.includes("@") ? input.to : `${input.to.replace(/\D/g, "")}@s.whatsapp.net`;
        const quoted = input.quotedMessageId ? { key: { remoteJid: jid, id: input.quotedMessageId, fromMe: Boolean(input.quotedFromMe) } } : undefined;
        const sent = await session.socket.sendMessage(jid, { text: input.text ?? "" }, quoted ? { quoted } : undefined);
        return json(response, 200, { messageId: sent?.key?.id ?? "" });
      }

      if (request.method === "POST" && parts[2] === "reactions") {
        if (session.status !== "connected" || !session.socket) return json(response, 409, { error: "session_not_connected" });
        const input = await readBody(request);
        const jid = input.to.includes("@") ? input.to : `${input.to.replace(/\D/g, "")}@s.whatsapp.net`;
        await session.socket.sendMessage(jid, { react: { text: input.emoji ?? "", key: { remoteJid: jid, id: input.messageId, fromMe: Boolean(input.fromMe) } } });
        return json(response, 200, { reacted: true });
      }

      if (request.method === "POST" && parts[2]?.endsWith(":delete")) {
        if (session.status !== "connected" || !session.socket) return json(response, 409, { error: "session_not_connected" });
        const input = await readBody(request);
        const jid = input.to.includes("@") ? input.to : `${input.to.replace(/\D/g, "")}@s.whatsapp.net`;
        const messageId = parts[2].slice(0, -":delete".length);
        await session.socket.sendMessage(jid, { delete: { remoteJid: jid, id: messageId, fromMe: Boolean(input.fromMe) } });
        return json(response, 200, { deleted: true });
      }
    }

    return json(response, 404, { error: "not_found" });
  } catch (error) {
    console.error(error);
    return json(response, 500, { error: "bridge_error" });
  }
});

server.listen(port, "0.0.0.0", () => console.log(`VANTA WhatsApp bridge listening on ${port}`));
