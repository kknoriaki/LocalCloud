export interface FileEntry {
  id: string;
  path: string;
  name: string;
  extension: string;
  mime: string;
  size: number;
  kind: "photo" | "video" | "audio" | "file";
  modifiedAt: string;
  importedAt: string;
  takenAt: string | null;
  hash: string | null;
  favorite: boolean;
  trashed: boolean;
  width: number | null;
  height: number | null;
  duration: number | null;
  metadata: string | null;
  livePartner: string | null;
}
export interface Folder {
  path: string;
  name: string;
}
export interface Album {
  coverReady: boolean;
  id: string;
  name: string;
  count: number;
  cover: string | null;
}
export interface Settings {
  version: string;
  updateMethod: string;
  storageRoot: string | null;
  port: number;
  allowLan: boolean;
  launchAtStartup: boolean;
  concurrentUploads: number;
  duplicateBehavior: string;
  trashRetentionDays: number;
  pinEnabled: boolean;
  local: boolean;
  addresses: string[];
  mdns: boolean;
  ffmpeg: boolean;
  indexing: boolean;
  lastScan: string | null;
}
export async function api<T = unknown>(
  path: string,
  body?: unknown,
  method?: string,
): Promise<T> {
  const res = await fetch("/api" + path, {
    method: method || (body !== undefined ? "POST" : "GET"),
    headers: body !== undefined ? { "Content-Type": "application/json" } : {},
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    if (res.status === 401) window.dispatchEvent(new Event("lc-lock"));
    let data;
    try {
      data = await res.json();
    } catch {
      data = {
        message:
          res.status === 429
            ? "Слишком много попыток. Подождите минуту."
            : "Не удалось связаться с LocalCloud.",
      };
    }
    throw new Error(data.message || "Операция не выполнена.");
  }
  return res.status === 204
    ? (undefined as T)
    : res.headers.get("content-type")?.includes("json")
      ? res.json()
      : (undefined as T);
}
export function bytes(value: number) {
  if (!value) return "0 Б";
  const i = Math.min(4, Math.floor(Math.log(value) / Math.log(1024)));
  return `${(value / 1024 ** i).toLocaleString("ru-RU", { maximumFractionDigits: i > 1 ? 1 : 0 })} ${["Б", "КБ", "МБ", "ГБ", "ТБ"][i]}`;
}
export function date(value: string) {
  return new Date(value).toLocaleDateString("ru-RU", {
    day: "numeric",
    month: "long",
    year: "numeric",
  });
}
export function duration(value: number | null) {
  if (value == null) return "";
  return `${Math.floor(value / 60)}:${Math.floor(value % 60)
    .toString()
    .padStart(2, "0")}`;
}

export function thumbnailUrl(f:FileEntry,size=512){let id=f.id;try{const m=JSON.parse(f.metadata||"{}");if(f.kind==="audio"&&m.coverId)id=m.coverId;}catch{}return `/api/files/${id}/thumbnail?size=${size}&v=${encodeURIComponent(f.modifiedAt)}-${f.metadata?"ready":"pending"}`}
