const APP_NAME = { cursor: "Cursor", codex: "Codex", claude: "Claude", "grok-bot": "Grok Bot", gpt: "GPT", mochi: "小猫" };
const REFRESH_MS = 5000;

let slotRecords = [];
let liveTasks = [];
let selectedId = "";

function islandMessage(payload) {
  const handler = window.webkit?.messageHandlers?.island;
  if (handler) handler.postMessage(payload);
}

const pendingCalls = new Map();
let callSeq = 0;

function islandCall(action, extra = {}) {
  return new Promise((resolve, reject) => {
    const rpc = String(++callSeq);
    const timer = setTimeout(() => {
      if (!pendingCalls.has(rpc)) return;
      pendingCalls.delete(rpc);
      reject(new Error("request failed"));
    }, 8000);
    pendingCalls.set(rpc, { resolve, reject, timer });
    islandMessage({ ...extra, action, rpc });
  });
}

window.mochiReply = (rpc, payload) => {
  const item = pendingCalls.get(String(rpc));
  if (!item) return;
  clearTimeout(item.timer);
  pendingCalls.delete(String(rpc));
  item.resolve(payload);
};

function taskSource(task) {
  const value = String((task && task.source) || "").trim().toLowerCase();
  if (value === "codex") return "codex";
  if (value === "claude" || value === "claude-code") return "claude";
  if (value === "grok-bot" || value === "grok" || value === "grokbot" || value === "grok bot") return "grok-bot";
  if (value === "gpt" || value === "chatgpt" || value === "openai") return "gpt";
  if (value === "mochi" || value === "cat" || value === "小猫") return "mochi";
  return "cursor";
}

function normalizeSlot(raw) {
  const id = String((raw && raw.id) || "").trim();
  if (!id) return null;
  const source = taskSource(raw);
  return {
    id,
    source,
    title: String((raw && raw.title) || "").trim() || "New chat",
    created: Number(raw && raw.created) || 0,
  };
}

function ensureSlotFromTask(task) {
  const id = String((task && task.id) || "");
  if (!id) return;
  const source = taskSource(task);
  const existing = slotRecords.find((slot) => slot.id === id);
  if (existing) {
    existing.source = source;
    if (task.title) existing.title = String(task.title);
    return;
  }
  slotRecords.push({
    id,
    source,
    title: String(task.title || "").trim() || "New chat",
    created: Number(task.updated) || 0,
  });
}

function applySnapshot(tasks, state) {
  const list = Array.isArray(tasks) ? tasks : [];
  if (state && Array.isArray(state.slots)) {
    slotRecords = state.slots.map(normalizeSlot).filter(Boolean);
  }
  if (state && typeof state.selected === "string" && state.selected) selectedId = state.selected;
  liveTasks = list;
  list.forEach(ensureSlotFromTask);
  if (!slotRecords.some((slot) => slot.id === selectedId)) {
    selectedId = slotRecords[0] ? slotRecords[0].id : "";
  }
}

window.__mochiClearChoices = () => {};

window.__mochiSlots = () =>
  slotRecords.map((slot) => ({
    id: slot.id,
    source: slot.source,
    app: APP_NAME[slot.source] || "Cursor",
    selected: slot.id === selectedId,
    face: `pets/agent-${slot.source === "grok-bot" ? "grok" : slot.source}.svg`,
    popupApp: slot.id === selectedId ? APP_NAME[slot.source] || "Cursor" : "",
  }));

window.__capsuleChrome = () => ({ selected: selectedId, menuCount: 0 });

window.pushTasks = (tasks, state) => {
  applySnapshot(tasks, state || {});
};

window.applyCapsuleChrome = function applyCapsuleChrome(info) {
  if (info && typeof info.selected === "string" && info.selected) selectedId = info.selected;
  reportHotRects();
};

window.applyCapsuleEvent = function applyCapsuleEvent(event) {
  if (!event || event.kind !== "select") return;
  selectedId = event.id || selectedId;
};

window.applyPrefs = function applyPrefs() {};
window.applyScreenLayout = function applyScreenLayout() {};

window.__mochiFireChoice = function fireChoice() {
  const task = liveTasks.find((item) => item.id === "choice-probe") || liveTasks[0] || {};
  const started = performance.now();
  window.__mochiMeasure = { choiceBubbleDone: false, choiceReplyDone: false };
  window.__mochiMeasure.choiceBubbleMs = performance.now() - started;
  window.__mochiMeasure.choiceBubbleDone = true;
  const option = (task.options && task.options[0]) || { id: "allow", label: "Allow" };
  islandCall("cursorChoice", {
    id: task.id || "",
    kind: task.kind || "",
    request_id: task.request_id || "",
    question_id: task.question_id || "",
    option_id: option.id || "allow",
    label: option.label || "Allow",
  }).then(() => {
    window.__mochiMeasure.choiceReplyMs = performance.now() - started;
    window.__mochiMeasure.choiceReplyDone = true;
  }).catch(() => {
    window.__mochiMeasure.choiceReplyMs = performance.now() - started;
    window.__mochiMeasure.choiceReplyDone = true;
  });
};

function reportHotRects() {
  islandMessage({ action: "hotRects", rects: "[]" });
  islandMessage({ action: "glass", rects: "[]" });
}

async function refresh() {
  try {
    const state = await islandCall("getState");
    const tasks = await islandCall("getTasks");
    applySnapshot(Array.isArray(tasks) ? tasks : [], state || {});
  } catch (error) {
    return;
  }
  reportHotRects();
}

if (window.webkit?.messageHandlers?.island) {
  refresh();
  setInterval(refresh, REFRESH_MS);
}
requestAnimationFrame(reportHotRects);
