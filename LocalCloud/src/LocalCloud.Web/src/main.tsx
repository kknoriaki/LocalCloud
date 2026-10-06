import { PhotoCanvas } from "./PhotoCanvas";
import { MusicProvider, MiniPlayer, useMusic, musicTags } from "./Music";
import {
  BackupPanel,
  SharingPanel,
  OrganizePanel,
  ProcessPanel,
  UploadHistory,
  BulkRename,
  PhotoEditPanel,
  TrackEditor,
  Collections,
  defaultRecipe,
  type Recipe,
} from "./FeaturePanels";
import { SharedLibrary } from "./SharedLibrary";
const PdfViewer = React.lazy(() => import("./PdfViewer"));
import { NetworkPanel } from "./NetworkPanel";
import { MediaPlayer } from "./MediaPlayer";
import React, { useCallback, useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import { useVirtualizer } from "@tanstack/react-virtual";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { QRCodeSVG } from "qrcode.react";
import {
  Cloud,
  Music2,
  Images,
  Film,
  Folder,
  Heart,
  Clock,
  Trash2,
  HardDrive,
  Settings as SettingsIcon,
  Upload,
  Plus,
  Search,
  ChevronRight,
  ChevronLeft,
  ArrowDownToLine,
  MoreHorizontal,
  Grid2X2,
  List,
  Check,
  X,
  FolderPlus,
  Smartphone,
  Wifi,
  ArrowUpRight,
  Sun,
  Moon,
  Monitor,
  RefreshCw,
  Copy,
  Move,
  Info,
  Pause,
  Play,
  RotateCcw,
  CheckCircle2,
  AlertCircle,
  ZoomIn,
  ZoomOut,
  Lock,
  LogOut,
  Album as AlbumIcon,
  FileText,
  ArrowLeft,
  SlidersHorizontal,
  Loader2,
  ExternalLink,
} from "lucide-react";
import {
  api,
  bytes,
  date,
  duration,
  thumbnailUrl,
  type FileEntry,
  type Album,
  type Folder as FolderType,
  type Settings,
} from "./api";
import {
  addUploads,
  addDirectoryUploads,
  useUploads,
  uploadAction,
  clearFinished,
  configureUploads,
} from "./uploads";
import "@fontsource-variable/manrope";
import "./styles.css";

type View =
  | "photos"
  | "videos"
  | "audio"
  | "files"
  | "albums"
  | "favorites"
  | "recent"
  | "trash"
  | "storage"
  | "settings";
const navigation = [
  { id: "photos", name: "Фото", icon: Images },
  { id: "videos", name: "Видео", icon: Film },
  { id: "audio", name: "Музыка", icon: Music2 },
  { id: "files", name: "Файлы", icon: Folder },
  { id: "albums", name: "Альбомы", icon: AlbumIcon },
  { id: "favorites", name: "Избранное", icon: Heart },
  { id: "recent", name: "Недавние", icon: Clock },
  { id: "trash", name: "Корзина", icon: Trash2 },
] as const;
function IconButton({
  title,
  children,
  onClick,
  className = "",
  disabled = false,
}: {
  title: string;
  children: React.ReactNode;
  onClick?: () => void;
  className?: string;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      aria-label={title}
      title={title}
      className={"icon-button " + className}
      onClick={onClick}
      disabled={disabled}
    >
      {children}
    </button>
  );
}
function Dialog({
  title,
  children,
  onClose,
}: {
  title: string;
  children: React.ReactNode;
  onClose: () => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const closeRef=useRef(onClose);closeRef.current=onClose;
  useEffect(() => {
    const before = document.activeElement as HTMLElement;
    const el = ref.current;
    const first = el?.querySelector<HTMLElement>("input,select,button");
    first?.focus();
    function key(e: KeyboardEvent) {
      if (e.key === "Escape") {e.preventDefault();closeRef.current();}
      if (e.key === "Tab" && el) {
        const all = Array.from(
          el.querySelectorAll<HTMLElement>(
            "button:not(:disabled),input,select,a[href]",
          ),
        );
        if (e.shiftKey && document.activeElement === all[0]) {
          e.preventDefault();
          all.at(-1)?.focus();
        } else if (!e.shiftKey && document.activeElement === all.at(-1)) {
          e.preventDefault();
          all[0]?.focus();
        }
      }
    }
    document.addEventListener("keydown", key);
    return () => {
      document.removeEventListener("keydown", key);
      before?.focus();
    };
  }, []);
  return (
    <div
      className="overlay"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        className="dialog"
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-label={title}
      >
        <div className="dialog-head">
          <h2>{title}</h2>
          <IconButton title="Закрыть" onClick={onClose}>
            <X size={20} />
          </IconButton>
        </div>
        {children}
      </div>
    </div>
  );
}
function App() {
  const music = useMusic();
  const [feature, setFeature] = useState<string | null>(null),
    [trackEdit, setTrackEdit] = useState<FileEntry | null>(null),
    [collectionName, setCollectionName] = useState("");
  const [view, setView] = useState<View>("photos"),
    [folder, setFolder] = useState(""),
    [album, setAlbum] = useState<Album | null>(null),
    [search, setSearch] = useState(""),
    [debounced, setDebounced] = useState(""),
    [sort, setSort] = useState("date"),
    [missingTags,setMissingTags] = useState(false),
    [layout, setLayout] = useState("grid"),
    [page, setPage] = useState(0),
    [files, setFiles] = useState<FileEntry[]>([]),
    [total, setTotal] = useState(0),
    [folders, setFolders] = useState<FolderType[]>([]),
    [albums, setAlbums] = useState<Album[]>([]),
    [selected, setSelected] = useState<Set<string>>(new Set()),
    [selectionMode, setSelectionMode] = useState(false),
    [loading, setLoading] = useState(true),
    [failure, setFailure] = useState(""),
    [settings, setSettings] = useState<Settings | null>(null),
    [storage, setStorage] = useState<any>(null),
    [status, setStatus] = useState<any>(null),
    [locked, setLocked] = useState(false),
    [theme, setTheme] = useState(localStorage.getItem("lc-theme") || "system"),
    [viewer, setViewer] = useState<FileEntry | null>(null),
    [uploadsOpen, setUploadsOpen] = useState(
      new URLSearchParams(location.search).has("uploads"),
    ),
    [uploadDialog, setUploadDialog] = useState(false),
    [uploadFolder, setUploadFolder] = useState("Photos"),
    [dialog, setDialog] = useState<{
      type: string;
      value?: string;
      ids?: string[];
      path?: string;
    } | null>(null),
    [name, setName] = useState(""),
    [target, setTarget] = useState("Photos"),
    [tree, setTree] = useState<FolderType[]>([]),
    [toast, setToast] = useState<{ text: string; undo?: () => void } | null>(
      null,
    ),
    [more, setMore] = useState(false),
    [context, setContext] = useState<{
      x: number;
      y: number;
      file: FileEntry;
    } | null>(null),
    [filterOpen, setFilterOpen] = useState(false),
    [extension, setExtension] = useState(""),
    [after, setAfter] = useState(""),
    [before, setBefore] = useState(""),
    [minSize, setMinSize] = useState(""),
    [maxSize, setMaxSize] = useState(""),
    [connected, setConnected] = useState(true),
    [phone, setPhone] = useState(false);
  const queue = useUploads();
  const [remoteUploads, setRemoteUploads] = useState<any[]>([]);
  const directoryPicker = useRef<HTMLInputElement>(null);
  const picker = useRef<HTMLInputElement>(null),
    searchRef = useRef<HTMLInputElement>(null),
    scroll = useRef<HTMLDivElement>(null),
    gallery = useRef<HTMLDivElement>(null);
  const lastIndex = useRef(0);
  const loadGeneration=useRef(0);
  const [cols, setCols] = useState(4);
  const [dragging, setDragging] = useState(false);
  const longPressFired = useRef(false);
  const gesture = useRef<{ x: number; y: number } | null>(null);
  const notify = useCallback((text: string, undo?: () => void) => {
    setToast({ text, undo });
  }, []);
  useEffect(() => {
    if (!toast) return;
    const timer = setTimeout(() => setToast(null), 6500);
    return () => clearTimeout(timer);
  }, [toast]);
  useEffect(() => {
    const m = matchMedia("(prefers-color-scheme: dark)");
    const apply = () =>
      (document.documentElement.dataset.theme =
        theme === "system" ? (m.matches ? "dark" : "light") : theme);
    apply();
    m.addEventListener("change", apply);
    localStorage.setItem("lc-theme", theme);
    return () => m.removeEventListener("change", apply);
  }, [theme]);
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebounced(search);
      setPage(0);
    }, 250);
    return () => clearTimeout(timer);
  }, [search]);
  const loadConfig = useCallback(async () => {
    const s = await api<any>("/status");
    setStatus(s);
    setLocked(s.requiresPin);
    if (!s.requiresPin) {
      const [config, space] = await Promise.all([
        api<Settings>("/settings"),
        api("/storage"),
      ]);
      setSettings(config);
      setStorage(space);
      configureUploads(config.concurrentUploads, config.duplicateBehavior);
    }
  }, []);
  useEffect(() => {
    void loadConfig().catch((e) => setFailure(e.message));
    const lock = () => {
      music.close();
      setLocked(true);
    };
    window.addEventListener("lc-lock", lock);
    return () => window.removeEventListener("lc-lock", lock);
  }, [loadConfig]);
  const load = useCallback(
    async (silent = false) => {
      if (locked || !status) return;
      const generation=++loadGeneration.current;
      if (!silent) setLoading(true);
      try {
        const query = new URLSearchParams({
          view: view === "albums" ? "all" : view,
          offset: String(page * 120),
          limit: "120",
          sort,
        });
        if (view === "files") query.set("folder", folder);
        if (album) query.set("album", album.id);
        if (debounced) query.set("search", debounced);
        if(view==="audio"&&missingTags)query.set("missingTags","true");
        if (extension) query.set("extension", extension);
        if (after) query.set("after", after);
        if (before) query.set("before", before + "T23:59:59Z");
        if (minSize)
          query.set("minSize", String(Number(minSize) * 1024 * 1024));
        if (maxSize)
          query.set("maxSize", String(Number(maxSize) * 1024 * 1024));
        const [data, fs, as, st] = await Promise.all([
          api<{ items: FileEntry[]; total: number }>("/files?" + query),
          api<FolderType[]>("/folders?path=" + encodeURIComponent(folder)),
          api<Album[]>("/albums"),
          api("/storage"),
        ]);
        if(generation!==loadGeneration.current)return;
        setFiles(data.items);
        setViewer((current) =>
          current
            ? data.items.find((item) => item.id === current.id) || current
            : null,
        );
        setTotal(data.total);
        setFolders(fs);
        setAlbums(as);
        setStorage(st);
        setFailure("");
      } catch (e) {
        if(generation===loadGeneration.current)setFailure((e as Error).message);
      } finally {
        if(generation===loadGeneration.current)setLoading(false);
      }
    },
    [
      view,
      folder,
      album,
      debounced,
      sort,
      page,
      locked,
      status,
      extension,
      after,
      before,
      minSize,
      maxSize,
      missingTags,
    ],
  );
  const loadRef=useRef(load);loadRef.current=load;
  useEffect(() => {
    void load();
  }, [load]);
  useEffect(() => {
    if (locked || !status) return;
    let alive = true;
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/library")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Error)
      .build();
    let timer: ReturnType<typeof setTimeout>;
    const refresh = () => {
      clearTimeout(timer);
      timer = setTimeout(() => {
        if(!document.hidden)void loadRef.current(true);
      }, 800);
    };
    connection.on("changed", refresh);
    connection.onreconnecting(() => setConnected(false));
    connection.onreconnected(() => {
      setConnected(true);
      refresh();
    });
    void connection
      .start()
      .then(() => {
        if (alive) setConnected(true);
      })
      .catch(() => setConnected(false));
    const poll = setInterval(()=>{if(connection.state!=="Connected"&&!document.hidden)refresh();}, 30000);
    const visible=()=>{if(!document.hidden)refresh();};
    document.addEventListener("visibilitychange",visible);
    window.addEventListener("lc-refresh", refresh);
    return () => {
      alive = false;
      clearTimeout(timer);
      clearInterval(poll);
      document.removeEventListener("visibilitychange",visible);
      window.removeEventListener("lc-refresh", refresh);
      void connection.stop();
    };
  }, [locked, status]);
  useEffect(() => {
    const el = scroll.current;
    if (!el) return;
    const ro = new ResizeObserver(([entry]) =>
      setCols(Math.max(2, Math.floor(entry.contentRect.width / 205))),
    );
    ro.observe(el);
    return () => ro.disconnect();
  }, [view, locked, status]);
  const navigate = (v: View) => {
    setView(v);
    setAlbum(null);
    setFolder("");
    setPage(0);
    setSelected(new Set());
    setSelectionMode(false);
    setMore(false);
    setSearch("");
    scroll.current?.scrollTo(0, 0);
  };
  const changeFolder = (path: string) => {
    setFolder(path);
    setPage(0);
    setSelected(new Set());
    scroll.current?.scrollTo(0, 0);
  };
  async function act(action: string, ids = [...selected], value?: string) {
    try {
      const handled = new Set<string>();
      for (const id of ids) {
        if (handled.has(id)) continue;
        const result = await api<FileEntry>("/files/" + id + "/operate", {
          action,
          target: value,
        });
        handled.add(id);
        if (
          result.livePartner &&
          ["trash", "restore", "move", "copy", "purge"].includes(action)
        )
          handled.add(result.livePartner);
      }
      setSelected(new Set());
      void load(true);
      if (action === "trash") {
        notify("Перемещено в корзину", () => {
          void act("restore", ids);
        });
      } else notify(action === "restore" ? "Файлы восстановлены" : "Готово");
    } catch (e) {
      notify((e as Error).message);
    }
  }
  const openDialog = async (
    type: string,
    ids = [...selected],
    value = "",
    path?: string,
  ) => {
    setName(value);
    setTarget(folder || "Photos");
    setDialog({ type, ids, value, path });
    if (["move", "copy", "moveFolder", "copyFolder", "upload"].includes(type)) {
      try {
        setTree(await api<FolderType[]>("/folders/tree"));
      } catch (e) {
        notify((e as Error).message);
      }
    }
  };
  async function submitDialog() {
    if (!dialog) return;
    try {
      switch (dialog.type) {
        case "folder":
          await api("/folders", {
            parent: view === "files" ? folder : "Photos",
            name,
          });
          notify("Папка создана");
          break;
        case "album":
          await api("/albums", { name });
          notify("Альбом создан");
          break;
        case "rename":
          await act("rename", dialog.ids, name);
          break;
        case "move":
        case "copy":
          await act(dialog.type, dialog.ids, target);
          break;
        case "trash":
        case "restore":
        case "purge":
          await act(dialog.type, dialog.ids);
          break;
        case "addAlbum":
          await api("/albums/" + target + "/items", { ids: dialog.ids });
          notify("Добавлено в альбом");
          break;
        case "emptyTrash":
          await api("/trash/empty", {});
          notify("Корзина очищена");
          break;
        case "deleteAlbum":
          await api("/albums/" + album?.id, undefined, "DELETE");
          setAlbum(null);
          notify("Альбом удалён. Оригиналы сохранены.");
          break;
        case "renameAlbum":
          await api("/albums/" + album?.id + "/rename", { name });
          if (album) setAlbum({ ...album, name });
          notify("Альбом переименован");
          break;
        case "renameFolder":
          await api("/folders/operate", {
            path: dialog.path,
            action: "rename",
            target: name,
          });
          notify("Папка переименована");
          break;
        case "deleteFolder": {
          const result = await api<{ ids: string[] }>("/folders/operate", {
            path: dialog.path,
            action: "trash",
          });
          notify("Папка перемещена в корзину", () => {
            void act("restore", result.ids);
          });
          break;
        }
        case "copyFolder":
          await api("/folders/operate", {
            path: dialog.path,
            action: "copy",
            target,
          });
          notify("Папка скопирована");
          break;
        case "moveFolder":
          await api("/folders/operate", {
            path: dialog.path,
            action: "move",
            target,
          });
          notify("Папка перемещена");
          break;
      }
      setDialog(null);
      void load(true);
    } catch (e) {
      notify((e as Error).message);
    }
  }
  function select(f: FileEntry, index: number, e?: React.MouseEvent) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (e?.shiftKey) {
        const [a, b] = [lastIndex.current, index].sort((a, b) => a - b);
        files.slice(a, b + 1).forEach((f) => next.add(f.id));
      } else if (next.has(f.id)) next.delete(f.id);
      else next.add(f.id);
      lastIndex.current = index;
      return next;
    });
  }
  useEffect(() => {
    function key(e: KeyboardEvent) {
      if (
        (e.target as HTMLElement).matches("input,textarea,select") ||
        dialog ||
        locked
      )
        return;
      if (viewer) {
        if (e.key === "Escape") setViewer(null);
        if (e.key === "ArrowRight" || e.key === "ArrowLeft") {
          const i = files.findIndex((f) => f.id === viewer.id);
          setViewer(
            files[
              (i + (e.key === "ArrowRight" ? 1 : -1) + files.length) %
                files.length
            ],
          );
        }
        return;
      }
      if ((e.ctrlKey || e.metaKey) && e.key === "a") {
        e.preventDefault();
        setSelected(new Set(files.map((f) => f.id)));
      }
      if ((e.ctrlKey || e.metaKey) && e.key === "f") {
        e.preventDefault();
        searchRef.current?.focus();
      }
      if (e.key === "Escape") {
        setSelected(new Set());
        setSelectionMode(false);
        setContext(null);
      }
      if (e.key === "Delete" && selected.size)
        void openDialog(view === "trash" ? "purge" : "trash");
    }
    document.addEventListener("keydown", key);
    return () => document.removeEventListener("keydown", key);
  }, [files, selected, viewer, dialog, locked, view]);
  useEffect(() => {
    const close = () => setContext(null);
    window.addEventListener("click", close);
    return () => window.removeEventListener("click", close);
  }, []);
  const rows = Math.ceil(files.length / cols);
  const virtualizer = useVirtualizer({
    count: rows,
    getScrollElement: () => scroll.current,
    estimateSize: () => (layout === "list" ? cols * 66 : 250),
    overscan: 4,
    scrollMargin: gallery.current?.offsetTop || 0,
  });
  useEffect(() => {
    if (!uploadsOpen || locked) return;
    const update = () =>
      void api<any[]>("/uploads")
        .then(setRemoteUploads)
        .catch(() => {});
    update();
    const timer = setInterval(update, 1500);
    return () => clearInterval(timer);
  }, [uploadsOpen, locked]);
  const remoteTasks = remoteUploads.filter(
    (t) =>
      !queue.some(
        (q) => q.serverId === t.id || q.upload?.url?.endsWith("/" + t.id),
      ),
  );
  const active =
    remoteTasks.length +
    queue.filter(
      (t) => !["complete", "skipped", "cancelled"].includes(t.status),
    ).length;
  const uploaded =
      queue.reduce((sum, t) => sum + t.bytes, 0) +
      remoteTasks.reduce((sum, t) => sum + Number(t.offset || 0), 0),
    uploadTotal =
      queue.reduce((sum, t) => sum + t.total, 0) +
      remoteTasks.reduce((sum, t) => sum + Number(t.size || 0), 0);
  const [phoneAddress, setPhoneAddress] = useState("");
  const serverAddress =
    phoneAddress || settings?.addresses[0] || location.origin;
  function receive(files: File[]) {
    setUploadDialog(false);
    if (files.length) {
      addUploads(files, uploadFolder);
      setUploadsOpen(true);
    }
  }
  if (!status)
    return (
      <div className="startup">
        <Cloud size={48} />
        {failure ? (
          <>
            <h2>Не удалось подключиться</h2>
            <p>{failure}</p>
            <button className="primary" onClick={() => location.reload()}>
              Повторить
            </button>
          </>
        ) : (
          <>
            <h2>Ваше пространство открывается</h2>
            <Loader2 className="spin" />
          </>
        )}
      </div>
    );
  if (locked)
    return (
      <Unlock
        onUnlock={() => {
          setLocked(false);
          void loadConfig();
        }}
      />
    );
  if (!status.configured)
    return (
      <Setup
        settings={settings}
        onDone={() => {
          void loadConfig();
        }}
      />
    );
  return (
    <div
      className={"app " + (music.queue.length ? "has-music" : "")}
      onDragOver={(e) => {
        if (e.dataTransfer.types.includes("Files")) {
          e.preventDefault();
          setDragging(true);
        }
      }}
      onDragLeave={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node))
          setDragging(false);
      }}
      onDrop={(e) => {
        e.preventDefault();
        setDragging(false);
        addUploads(
          Array.from(e.dataTransfer.files),
          view === "files" ? folder : "Photos",
        );
        setUploadsOpen(true);
      }}
    >
      <input
        type="file"
        multiple
        hidden
        ref={directoryPicker}
        {...({ webkitdirectory: "" } as any)}
        onChange={(e) => {
          const picked = Array.from(e.currentTarget.files || []);
          setUploadDialog(false);
          void addDirectoryUploads(picked, uploadFolder)
            .then(() => setUploadsOpen(true))
            .catch((e) => notify(e.message));
          e.currentTarget.value = "";
        }}
      />
      <input
        ref={picker}
        type="file"
        multiple
        hidden
        onChange={(e) => {
          receive(Array.from(e.target.files || []));
          e.target.value = "";
        }}
      />
      <aside className="sidebar">
        <a href="/" className="brand" aria-label="LocalCloud">
          <span className="brand-mark">
            <Cloud size={24} />
          </span>
          <span>
            LocalCloud<small>Личное пространство</small>
          </span>
        </a>
        <div className="side-caption">БИБЛИОТЕКА</div>
        <nav>
          {navigation.map((n) => (
            <button
              key={n.id}
              className={view === n.id ? "nav active" : "nav"}
              onClick={() => navigate(n.id)}
            >
              <n.icon size={19} />
              <span>{n.name}</span>
              {n.id === "photos" &&
                storage?.groups?.find((g: any) => g.kind === "photo") && (
                  <small>
                    {Number(
                      storage.groups.find((g: any) => g.kind === "photo").count,
                    ).toLocaleString("ru-RU")}
                  </small>
                )}
            </button>
          ))}
        </nav>
        <div className="side-line" />
        <button className="nav" onClick={()=>setFeature("history")}><Upload size={19}/>Загрузки</button>
        <button className="nav" onClick={()=>setFeature("tools")}><SlidersHorizontal size={19}/>Инструменты</button>
        <button
          className={view === "storage" ? "nav active" : "nav"}
          onClick={() => navigate("storage")}
        >
          <HardDrive size={19} />
          Хранилище
        </button>
        <button
          className={view === "settings" ? "nav active" : "nav"}
          onClick={() => navigate("settings")}
        >
          <SettingsIcon size={19} />
          Настройки
        </button>
        <div className="sidebar-bottom">
          <button className="phone-card" onClick={() => setPhone(true)}>
            <Smartphone size={23} />
            <span>
              Открыть на iPhone<small>Без проводов. Рядом с вами.</small>
            </span>
            <ArrowUpRight size={16} />
          </button>
          <div className="capacity">
            <div>
              <span>На вашем диске</span>
              <span>{storage ? bytes(storage.free) : "—"} свободно</span>
            </div>
            <div className="capacity-bar">
              <i
                style={{
                  width: `${storage ? (storage.used / storage.total) * 100 : 0}%`,
                }}
              />
            </div>
            <small>
              {storage ? bytes(storage.libraryBytes) : "—"} в LocalCloud
            </small>
          </div>
          <div className="local-status">
            <i className={connected ? "online" : "offline"} />
            {connected ? "Работает в домашней сети" : "Переподключение…"}
            <Lock size={12} />
          </div>
        </div>
      </aside>
      <MiniPlayer onEdit={setTrackEdit} />
      <div className="workspace">
        <header className="topbar">
          <div className="mobile-brand">
            <Cloud size={23} />
            LocalCloud
          </div>
          <div className="top-label">
            <span className="status-dot" />
            Ваши файлы. Только у вас.
          </div>
          <label className="search">
            <Search size={18} />
            <input
              ref={searchRef}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Поиск в вашем пространстве"
              aria-label="Поиск файлов"
            />
            <kbd>Ctrl F</kbd>
            {search && (
              <IconButton title="Очистить поиск" onClick={() => setSearch("")}>
                <X size={15} />
              </IconButton>
            )}
          </label>
          <div className="top-actions">
            <IconButton
              title="История загрузок"
              onClick={() => setFeature("history")}
            >
              <Clock size={19} />
            </IconButton>
            <IconButton
              title="Подключить телефон"
              onClick={() => setPhone(true)}
            >
              <Smartphone size={19} />
            </IconButton>
            <IconButton
              title="Сменить тему"
              onClick={() => setTheme(theme === "dark" ? "light" : "dark")}
            >
              {theme === "dark" ? <Sun size={19} /> : <Moon size={19} />}
            </IconButton>
            <button
              className="avatar"
              onClick={() => navigate("settings")}
              aria-label="Настройки"
            >
              LC
            </button>
          </div>
        </header>
        <main ref={scroll} className="main">
          <div className="page-heading">
            <div>
              <p className="eyebrow">ВАШЕ ЛИЧНОЕ ОБЛАКО</p>
              <h1>
                {album
                  ? album.name
                  : view === "files" && folder
                    ? folder.split("/").at(-1)
                    : navigation.find((n) => n.id === view)?.name ||
                      (view === "audio"
                        ? "Музыка"
                        : view === "storage"
                          ? "Хранилище"
                          : "Настройки")}
              </h1>
              <p className="subtitle">
                {view === "photos"
                  ? "Моменты, которые хочется сохранить."
                  : view === "audio"
                    ? "Любимые записи. В вашей домашней библиотеке."
                    : view === "videos"
                      ? "Всё, что осталось за кадром фотографии."
                      : view === "files"
                        ? "Обычные папки. Всё на своём месте."
                        : view === "albums"
                          ? "Ваши истории, собранные вместе."
                          : view === "favorites"
                            ? "Самое дорогое — всегда под рукой."
                            : view === "trash"
                              ? "Можно передумать. Файлы хранятся здесь " +
                                (settings?.trashRetentionDays || 30) +
                                " дней."
                              : view === "recent"
                                ? "Последние файлы в вашем пространстве."
                                : view === "storage"
                                  ? "Ваш диск. Ваши оригиналы. Ваш контроль."
                                  : "Пусть всё работает так, как вам удобно."}
              </p>
            </div>
            <div className="heading-actions">
              {view === "albums" && !album ? (
                <button
                  className="primary"
                  onClick={() => void openDialog("album")}
                >
                  <Plus size={18} />
                  Создать альбом
                </button>
              ) : view === "trash" ? (
                <button
                  className="secondary danger"
                  onClick={() => void openDialog("emptyTrash")}
                  disabled={!total}
                >
                  <Trash2 size={17} />
                  Очистить корзину
                </button>
              ) : !["settings", "storage"].includes(view) ? (
                <>
                  <button
                    className="secondary folder-create"
                    onClick={() => void openDialog("folder")}
                  >
                    <FolderPlus size={18} />
                    Создать папку
                  </button>
                  <button
                    className="primary"
                    onClick={() => {
                      setUploadFolder(
                        view === "files"
                          ? folder
                          : view === "audio"
                            ? "Music"
                            : view === "videos"
                              ? "Videos"
                              : "Photos",
                      );
                      void api<FolderType[]>("/folders/tree").then(setTree);
                      setUploadDialog(true);
                    }}
                  >
                    <Upload size={18} />
                    Загрузить
                  </button>
                </>
              ) : null}
            </div>
          </div>
          {view === "photos" && !search && page === 0 && (
            <div className="welcome-strip">
              <span className="welcome-icon">
                <Cloud size={28} />
              </span>
              <div>
                <strong>Воспоминания дома.</strong>
                <p>
                  С iPhone на компьютер — по вашему Wi-Fi. Оригиналы всегда
                  остаются с вами.
                </p>
              </div>
              <button onClick={() => setPhone(true)}>
                Подключить iPhone
                <ArrowUpRight size={16} />
              </button>
            </div>
          )}
          {view === "files" && (
            <div className="breadcrumbs">
              <button onClick={() => changeFolder("")}>
                <HardDrive size={16} />
                Хранилище
              </button>
              {folder
                .split("/")
                .filter(Boolean)
                .map((part, i) => (
                  <React.Fragment key={i}>
                    <ChevronRight size={14} />
                    <button
                      onClick={() =>
                        changeFolder(
                          folder
                            .split("/")
                            .slice(0, i + 1)
                            .join("/"),
                        )
                      }
                    >
                      {part}
                    </button>
                  </React.Fragment>
                ))}
            </div>
          )}
          {view === "albums" && album && (
            <div className="album-actions">
              <button
                className="text-button"
                onClick={() => {
                  setAlbum(null);
                  setPage(0);
                }}
              >
                <ArrowLeft size={17} />
                Все альбомы
              </button>
              <button
                className="text-button"
                onClick={() => void openDialog("renameAlbum", [], album.name)}
              >
                Переименовать
              </button>
              <button
                className="text-button danger"
                onClick={() => void openDialog("deleteAlbum")}
              >
                Удалить альбом
              </button>
            </div>
          )}
          {["storage", "settings"].includes(view) ? (
            view === "storage" ? (
              <StorageScreen
                storage={storage}
                settings={settings}
                onPhone={() => setPhone(true)}
              />
            ) : (
              <SettingsScreen
                settings={settings}
                theme={theme}
                onTheme={setTheme}
                notify={notify}
                reload={loadConfig}
              />
            )
          ) : (
            <>
              {view === "albums" && !album ? (
                <div>
                  <Collections
                    onOpen={(q) => {
                      navigate((q.View || q.view) as View);
                      setSearch(q.Search || q.search || "");
                      setExtension(q.Extension || q.extension || "");
                      setAfter((q.After || q.after || "").slice(0, 10));
                      setBefore((q.Before || q.before || "").slice(0, 10));
                      setMinSize(
                        q.MinSize || q.minSize
                          ? String((q.MinSize || q.minSize) / 1048576)
                          : "",
                      );
                      setMaxSize(
                        q.MaxSize || q.maxSize
                          ? String((q.MaxSize || q.maxSize) / 1048576)
                          : "",
                      );
                      setFolder(q.Folder || q.folder || "");
                    }}
                  />
                  <div className="album-grid">
                    {albums.map((a) => (
                      <button
                        className="album-card"
                        key={a.id}
                        onClick={() => {
                          setAlbum(a);
                          setPage(0);
                        }}
                      >
                        <div className="album-cover">
                          {a.cover ? (
                            <img
                              src={
                                "/api/files/" +
                                a.cover +
                                "/thumbnail?v=" +
                                a.coverReady
                              }
                              alt=""
                              loading="lazy"
                            />
                          ) : (
                            <AlbumIcon size={42} />
                          )}
                          <span>
                            <AlbumIcon size={20} />
                          </span>
                        </div>
                        <h3>{a.name}</h3>
                        <p>{a.count.toLocaleString("ru-RU")} файлов</p>
                      </button>
                    ))}
                    {!albums.length && !loading && (
                      <Empty
                        icon={AlbumIcon}
                        title="У каждой истории будет свой альбом"
                        text="Соберите фотографии в коллекции. Оригиналы останутся в своих папках."
                        action="Создать альбом"
                        onClick={() => void openDialog("album")}
                      />
                    )}
                  </div>
                </div>
              ) : (
                <>
                  {view==="audio"&&<div className="music-library-tools"><span>Название, исполнитель и альбом — в общем поиске сверху.</span><label><input type="checkbox" checked={missingTags} onChange={e=>{setMissingTags(e.target.checked);setPage(0);}}/>Без названия или исполнителя</label><button className="secondary" disabled={!files.length} onClick={()=>music.play(files,0)}><Play size={16}/>Слушать список</button></div>}
                  <div className="library-toolbar">
                    <div className="toolbar-left">
                      {selected.size > 1 && view !== "trash" && (
                        <IconButton
                          title="Массовое переименование"
                          onClick={() => setFeature("rename")}
                        >
                          <FileText size={18} />
                        </IconButton>
                      )}
                      {selected.size > 0 ? (
                        <>
                          <button
                            className="selected-count"
                            onClick={() => setSelected(new Set())}
                          >
                            <Check size={16} />
                            {selected.size} выбрано
                            <X size={14} />
                          </button>
                          {view === "trash" ? (
                            <>
                              <IconButton
                                title="Восстановить"
                                onClick={() => void act("restore")}
                              >
                                <RotateCcw size={18} />
                              </IconButton>
                              <IconButton
                                title="Удалить навсегда"
                                onClick={() => void openDialog("purge")}
                              >
                                <Trash2 size={18} />
                              </IconButton>
                            </>
                          ) : (
                            <>
                              <IconButton
                                title="В избранное"
                                onClick={() => void act("favorite")}
                              >
                                <Heart size={18} />
                              </IconButton>
                              <IconButton
                                title="Переместить"
                                onClick={() => void openDialog("move")}
                              >
                                <Move size={18} />
                              </IconButton>
                              <IconButton
                                title="Копировать"
                                onClick={() => void openDialog("copy")}
                              >
                                <Copy size={18} />
                              </IconButton>
                              <IconButton
                                title="Добавить в альбом"
                                onClick={() => {
                                  setTarget(albums[0]?.id || "");
                                  void openDialog("addAlbum");
                                }}
                              >
                                <AlbumIcon size={18} />
                              </IconButton>
                              <a
                                className="icon-button"
                                aria-label="Скачать выбранные файлы"
                                title="Скачать выбранные файлы"
                                href={
                                  "/api/downloads?ids=" +
                                  [...selected].join(",")
                                }
                              >
                                <ArrowDownToLine size={18} />
                              </a>
                              {album && (
                                <IconButton
                                  title="Убрать из альбома"
                                  onClick={() => {
                                    void Promise.all(
                                      [...selected].map((id) =>
                                        api(
                                          "/albums/" +
                                            album.id +
                                            "/items/" +
                                            id,
                                          undefined,
                                          "DELETE",
                                        ),
                                      ),
                                    )
                                      .then(() => {
                                        setSelected(new Set());
                                        void load(true);
                                        notify(
                                          "Файлы убраны из альбома. Оригиналы сохранены.",
                                        );
                                      })
                                      .catch((e) => notify(e.message));
                                  }}
                                >
                                  <X size={18} />
                                </IconButton>
                              )}
                              <IconButton
                                title="В корзину"
                                onClick={() => void openDialog("trash")}
                              >
                                <Trash2 size={18} />
                              </IconButton>
                              {selected.size === 1 && (
                                <IconButton
                                  title="Переименовать"
                                  onClick={() =>
                                    void openDialog(
                                      "rename",
                                      [...selected],
                                      files.find((f) => selected.has(f.id))
                                        ?.name,
                                    )
                                  }
                                >
                                  <FileText size={18} />
                                </IconButton>
                              )}
                            </>
                          )}
                        </>
                      ) : (
                        <>
                          {view === "files" && (
                            <button
                              className="text-button"
                              onClick={() => navigate("audio")}
                            >
                              <Music2 size={17} />
                              Музыка
                            </button>
                          )}
                          <span className="count">
                            {total.toLocaleString("ru-RU")}{" "}
                            {view === "photos"
                              ? "фотографий"
                              : view === "videos"
                                ? "видео"
                                : "файлов"}
                          </span>
                          <button
                            className="text-button"
                            onClick={() => setSelectionMode(!selectionMode)}
                          >
                            {selectionMode ? "Готово" : "Выбрать"}
                          </button>
                        </>
                      )}
                    </div>
                    <div className="toolbar-right">
                      <IconButton
                        title="Фильтры"
                        className={filterOpen ? "chosen" : ""}
                        onClick={() => setFilterOpen(!filterOpen)}
                      >
                        <SlidersHorizontal size={17} />
                      </IconButton>
                      <select
                        value={sort}
                        onChange={(e) => {
                          setSort(e.target.value);
                          setPage(0);
                        }}
                        aria-label="Сортировка"
                      >
                        <option value="date">Сначала новые</option>
                        <option value="oldest">Сначала старые</option>
                        {view==="audio"&&<><option value="title">По названию трека</option><option value="artist">По исполнителю</option><option value="album">По альбому</option></>}
                        <option value="name">По имени файла</option>
                        <option value="size">По размеру</option>
                      </select>
                      <div className="view-toggle">
                        <IconButton
                          title="Сетка"
                          className={layout === "grid" ? "chosen" : ""}
                          onClick={() => setLayout("grid")}
                        >
                          <Grid2X2 size={16} />
                        </IconButton>
                        <IconButton
                          title="Список"
                          className={layout === "list" ? "chosen" : ""}
                          onClick={() => setLayout("list")}
                        >
                          <List size={18} />
                        </IconButton>
                      </div>
                    </div>
                  </div>
                  {filterOpen && (
                    <div className="filters">
                      <label>
                        Тип файла
                        <input
                          placeholder="heic, jpg, mov…"
                          value={extension}
                          onChange={(e) => {
                            setExtension(e.target.value);
                            setPage(0);
                          }}
                        />
                      </label>
                      <label>
                        С даты
                        <input
                          type="date"
                          value={after}
                          onChange={(e) => {
                            setAfter(e.target.value);
                            setPage(0);
                          }}
                        />
                      </label>
                      <label>
                        По дату
                        <input
                          type="date"
                          value={before}
                          onChange={(e) => {
                            setBefore(e.target.value);
                            setPage(0);
                          }}
                        />
                      </label>
                      <label>
                        От МБ
                        <input
                          type="number"
                          min="0"
                          value={minSize}
                          onChange={(e) => {
                            setMinSize(e.target.value);
                            setPage(0);
                          }}
                        />
                      </label>
                      <label>
                        До МБ
                        <input
                          type="number"
                          min="0"
                          value={maxSize}
                          onChange={(e) => {
                            setMaxSize(e.target.value);
                            setPage(0);
                          }}
                        />
                      </label>
                      <button
                        className="text-button"
                        onClick={() => setFeature("collection")}
                      >
                        Сохранить как умный альбом
                      </button>
                      <button
                        className="text-button"
                        onClick={() => {
                          setExtension("");
                          setAfter("");
                          setBefore("");
                          setMinSize("");
                          setMaxSize("");
                          setPage(0);
                        }}
                      >
                        Сбросить
                      </button>
                    </div>
                  )}
                  {view === "files" && folders.length > 0 && !search && (
                    <div className="folders-grid">
                      {folders.map((f) => (
                        <div className="folder-card" key={f.path}>
                          <button onClick={() => changeFolder(f.path)}>
                            <Folder size={32} />
                            <span>
                              {f.name}
                              <small>Папка на компьютере</small>
                            </span>
                            <ChevronRight size={16} />
                          </button>
                          <div className="folder-options">
                            <button
                              aria-label={"Переместить папку " + f.name}
                              title="Переместить"
                              onClick={() =>
                                void openDialog(
                                  "moveFolder",
                                  [],
                                  f.name,
                                  f.path,
                                )
                              }
                            >
                              <Move size={15} />
                            </button>
                            <button
                              aria-label={"Копировать папку " + f.name}
                              title="Копировать"
                              onClick={() =>
                                void openDialog(
                                  "copyFolder",
                                  [],
                                  f.name,
                                  f.path,
                                )
                              }
                            >
                              <Copy size={15} />
                            </button>
                            <button
                              aria-label={"Переименовать папку " + f.name}
                              title="Переименовать"
                              onClick={() =>
                                void openDialog(
                                  "renameFolder",
                                  [],
                                  f.name,
                                  f.path,
                                )
                              }
                            >
                              <FileText size={15} />
                            </button>
                            <button
                              aria-label={"В корзину папку " + f.name}
                              title="В корзину"
                              onClick={() =>
                                void openDialog(
                                  "deleteFolder",
                                  [],
                                  f.name,
                                  f.path,
                                )
                              }
                            >
                              <Trash2 size={15} />
                            </button>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                  {failure && (
                    <div className="error-state">
                      <AlertCircle size={20} />
                      <span>{failure}</span>
                      <button onClick={() => void load()}>Повторить</button>
                    </div>
                  )}
                  {loading ? (
                    <div className="media-grid skeleton-grid">
                      {Array.from({ length: 12 }, (_, i) => (
                        <div className="skeleton" key={i} />
                      ))}
                    </div>
                  ) : !files.length ? (
                    <Empty
                      icon={
                        view === "trash"
                          ? Trash2
                          : view === "videos"
                            ? Film
                            : view === "files"
                              ? Folder
                              : Images
                      }
                      title={
                        search
                          ? "Ничего не нашлось"
                          : view === "trash"
                            ? "Здесь пока чисто"
                            : view === "files"
                              ? "В этой папке пока нет файлов"
                              : view === "favorites"
                                ? "Здесь будет самое любимое"
                                : "Место для ваших воспоминаний"
                      }
                      text={
                        search
                          ? "Попробуйте другое имя или измените фильтры."
                          : view === "trash"
                            ? "Удалённые файлы можно будет восстановить отсюда."
                            : view === "favorites"
                              ? "Нажмите на сердечко у фотографии, чтобы сохранить её здесь."
                              : "Загрузите фотографии с iPhone или перетащите файлы в это окно."
                      }
                      action={
                        search || view === "trash" || view === "favorites"
                          ? undefined
                          : "Загрузить файлы"
                      }
                      onClick={() => {
                        setUploadFolder(
                          view === "files"
                            ? folder
                            : view === "audio"
                              ? "Music"
                              : view === "videos"
                                ? "Videos"
                                : "Photos",
                        );
                        setUploadDialog(true);
                      }}
                    />
                  ) : (
                    <div
                      ref={gallery}
                      className="virtual-gallery"
                      style={{ height: virtualizer.getTotalSize() }}
                      onPointerDown={(e) => {
                        if (
                          !(e.target as HTMLElement).closest(
                            "[data-file-id]",
                          ) &&
                          e.pointerType === "mouse"
                        ) {
                          gesture.current = { x: e.clientX, y: e.clientY };
                          e.currentTarget.setPointerCapture(e.pointerId);
                        }
                      }}
                      onPointerUp={(e) => {
                        if (!gesture.current) return;
                        const a = gesture.current;
                        gesture.current = null;
                        if (
                          Math.abs(e.clientX - a.x) < 8 &&
                          Math.abs(e.clientY - a.y) < 8
                        )
                          return;
                        const ids = new Set(selected);
                        document
                          .querySelectorAll<HTMLElement>("[data-file-id]")
                          .forEach((el) => {
                            const r = el.getBoundingClientRect();
                            if (
                              r.right >= Math.min(a.x, e.clientX) &&
                              r.left <= Math.max(a.x, e.clientX) &&
                              r.bottom >= Math.min(a.y, e.clientY) &&
                              r.top <= Math.max(a.y, e.clientY)
                            )
                              ids.add(el.dataset.fileId!);
                          });
                        setSelected(ids);
                      }}
                    >
                      {virtualizer.getVirtualItems().map((row) => (
                        <div
                          key={row.key}
                          data-index={row.index}
                          ref={virtualizer.measureElement}
                          className={
                            "virtual-row " +
                            (layout === "list" ? "list-row" : "")
                          }
                          style={{
                            position: "absolute",
                            top: 0,
                            left: 0,
                            width: "100%",
                            transform: `translateY(${row.start - (virtualizer.options.scrollMargin || 0)}px)`,
                          }}
                        >
                          {(row.index === 0 ||
                            date(
                              files[row.index * cols]?.takenAt ||
                                files[row.index * cols]?.modifiedAt,
                            ) !==
                              date(
                                files[(row.index - 1) * cols]?.takenAt ||
                                  files[(row.index - 1) * cols]?.modifiedAt,
                              )) && (
                            <div className="date-divider">
                              {date(
                                files[row.index * cols]?.takenAt ||
                                  files[row.index * cols]?.modifiedAt,
                              )}
                            </div>
                          )}
                          <div
                            className={
                              layout === "grid" ? "media-grid" : "file-list"
                            }
                            style={
                              layout === "grid"
                                ? {
                                    gridTemplateColumns: `repeat(${cols},minmax(0,1fr))`,
                                  }
                                : undefined
                            }
                          >
                            {files
                              .slice(row.index * cols, (row.index + 1) * cols)
                              .map((f, j) => {
                                const index = row.index * cols + j;
                                return (
                                  <div
                                    className={
                                      "file-card " +
                                      (selected.has(f.id) ? "selected" : "") +
                                      (layout === "list" ? " file-line" : "")
                                    }
                                    key={f.id}
                                    data-file-id={f.id}
                                    onContextMenu={(e) => {
                                      e.preventDefault();
                                      setContext({
                                        x: Math.min(
                                          e.clientX,
                                          window.innerWidth - 220,
                                        ),
                                        y: Math.min(
                                          e.clientY,
                                          window.innerHeight - 330,
                                        ),
                                        file: f,
                                      });
                                    }}
                                  >
                                    <button
                                      className="file-open"
                                      aria-label={"Открыть " + f.name}
                                      onClick={(e) => {
                                        if (longPressFired.current) {
                                          longPressFired.current = false;
                                          return;
                                        }
                                        if (
                                          e.ctrlKey ||
                                          e.metaKey ||
                                          e.shiftKey ||
                                          selectionMode
                                        ) {
                                          select(f, index, e);
                                        } else if (f.extension === "pdf") { setViewer(f); } else if (f.kind === "file") {
                                          window.open(
                                            "/api/files/" +
                                              f.id +
                                              "/original?download=true",
                                            "_blank",
                                            "noopener",
                                          );
                                        } else if (f.kind === "audio") {
                                          const tracks = files.filter(
                                            (x) => x.kind === "audio",
                                          );
                                          music.play(
                                            tracks,
                                            tracks.findIndex(
                                              (x) => x.id === f.id,
                                            ),
                                          );
                                        } else setViewer(f);
                                      }}
                                      onTouchStart={() => {
                                        longPressFired.current = false;
                                        const timer = setTimeout(() => {
                                          longPressFired.current = true;
                                          setSelectionMode(true);
                                          select(f, index);
                                        }, 650);
                                        (window as any).lcPress = timer;
                                      }}
                                      onTouchEnd={() =>
                                        clearTimeout((window as any).lcPress)
                                      }
                                      onTouchMove={() =>
                                        clearTimeout((window as any).lcPress)
                                      }
                                    >
                                      <div className="thumbnail">
                                        {f.kind === "file" ||
                                        f.kind === "audio" ? (
                                          <div className="file-glyph">
                                            {f.kind === "audio" ? (
                                              musicTags(f).cover ? (
                                                <img
                                                  src={thumbnailUrl(f, 256)}
                                                  alt=""
                                                />
                                              ) : (
                                                <Music2 size={40} />
                                              )
                                            ) : (
                                              <FileText size={40} />
                                            )}
                                            <span>
                                              {f.extension.toUpperCase() ||
                                                "FILE"}
                                            </span>
                                          </div>
                                        ) : (
                                          <img
                                            src={thumbnailUrl(f)}
                                            alt={f.name}
                                            loading="lazy"
                                          />
                                        )}
                                        {f.kind === "video" && (
                                          <span className="video-badge">
                                            <Play
                                              size={12}
                                              fill="currentColor"
                                            />
                                            {duration(f.duration)}
                                          </span>
                                        )}
                                        {f.livePartner &&
                                          f.kind === "photo" && (
                                            <span className="live-badge">
                                              LIVE
                                            </span>
                                          )}
                                      </div>
                                      <div className="file-caption">
                                        <span>
                                          {view === "audio"
                                            ? musicTags(f).title
                                            : f.name}
                                        </span>
                                        <small>
                                          {view === "audio"
                                            ? musicTags(f).artist + " · "
                                            : ""}
                                          {bytes(f.size)}
                                          {layout === "list"
                                            ? " · " + date(f.modifiedAt)
                                            : ""}
                                        </small>
                                      </div>
                                    </button>
                                    <button
                                      className={
                                        "selection-dot " +
                                        (selected.has(f.id) ? "checked" : "")
                                      }
                                      aria-label={
                                        (selected.has(f.id)
                                          ? "Снять выбор "
                                          : "Выбрать ") + f.name
                                      }
                                      onClick={(e) => select(f, index, e)}
                                    >
                                      {selected.has(f.id) && (
                                        <Check size={15} />
                                      )}
                                    </button>
                                    {!f.trashed && (
                                      <button
                                        className={
                                          "favorite-button " +
                                          (f.favorite ? "favorited" : "")
                                        }
                                        aria-label={
                                          (f.favorite
                                            ? "Убрать из избранного "
                                            : "В избранное ") + f.name
                                        }
                                        onClick={() =>
                                          void act("favorite", [f.id])
                                        }
                                      >
                                        <Heart
                                          size={16}
                                          fill={
                                            f.favorite ? "currentColor" : "none"
                                          }
                                        />
                                      </button>
                                    )}
                                  </div>
                                );
                              })}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                  {total > 120 && (
                    <div className="pagination">
                      <button
                        className="secondary"
                        disabled={page === 0}
                        onClick={() => {
                          setPage(page - 1);
                          scroll.current?.scrollTo(0, 0);
                        }}
                      >
                        <ChevronLeft size={17} />
                        Назад
                      </button>
                      <span>
                        {page * 120 + 1}–{Math.min((page + 1) * 120, total)} из{" "}
                        {total.toLocaleString("ru-RU")}
                      </span>
                      <button
                        className="secondary"
                        disabled={(page + 1) * 120 >= total}
                        onClick={() => {
                          setPage(page + 1);
                          scroll.current?.scrollTo(0, 0);
                        }}
                      >
                        Далее
                        <ChevronRight size={17} />
                      </button>
                    </div>
                  )}
                </>
              )}
            </>
          )}
          <footer className="page-footer">
            <Lock size={12} />
            Ничего не отправляется во внешние сервисы
            <span>LOCALCLOUD / 1.0</span>
          </footer>
        </main>
      </div>
      <nav className="bottom-nav" aria-label="Мобильная навигация">
        <button
          className={view === "photos" ? "active" : ""}
          onClick={() => navigate("photos")}
        >
          <Images size={21} />
          <span>Фото</span>
        </button>
        <button
          className={view === "files" ? "active" : ""}
          onClick={() => navigate("files")}
        >
          <Folder size={21} />
          <span>Файлы</span>
        </button>
        <button
          className="mobile-add"
          aria-label="Загрузить или создать"
          onClick={() => {
            setUploadFolder(
              view === "files"
                ? folder
                : view === "audio"
                  ? "Music"
                  : view === "videos"
                    ? "Videos"
                    : "Photos",
            );
            void api<FolderType[]>("/folders/tree").then(setTree);
            setUploadDialog(true);
          }}
        >
          <Plus size={25} />
        </button>
        <button
          className={view === "albums" ? "active" : ""}
          onClick={() => navigate("albums")}
        >
          <AlbumIcon size={21} />
          <span>Альбомы</span>
        </button>
        <button className={more ? "active" : ""} onClick={() => setMore(!more)}>
          <MoreHorizontal size={22} />
          <span>Ещё</span>
        </button>
      </nav>
      {more && (
        <div className="mobile-more">
          {[
            ...navigation.filter(
              (n) => !["photos", "files", "albums"].includes(n.id),
            ),
            { id: "audio", name: "Музыка", icon: Music2 },
            { id: "storage", name: "Хранилище", icon: HardDrive },
            { id: "settings", name: "Настройки", icon: SettingsIcon },
          ].map((n) => (
            <button key={n.id} onClick={() => navigate(n.id as View)}>
              <n.icon size={20} />
              {n.name}
              <ChevronRight size={16} />
            </button>
          ))}
        </div>
      )}
      {queue.length > 0 && !uploadsOpen && (
        <button className="upload-pill" onClick={() => setUploadsOpen(true)}>
          <Upload size={18} />
          <span>
            {active ? `Загрузка · ${active} файлов` : "Загрузки завершены"}
          </span>
          <span>
            {uploadTotal ? Math.round((uploaded / uploadTotal) * 100) : 0}%
          </span>
        </button>
      )}
      {uploadsOpen && (
        <div className="upload-panel" role="region" aria-label="Загрузки">
          <div className="upload-head">
            <div>
              <h3>
                {active || remoteUploads.length
                  ? "Сохраняем ваши файлы"
                  : "Ваши файлы дома"}
              </h3>
              <p>
                {
                  queue.filter((t) =>
                    ["complete", "skipped"].includes(t.status),
                  ).length
                }{" "}
                / {queue.length + remoteTasks.length} · {bytes(uploaded)} /{" "}
                {bytes(uploadTotal)}
              </p>
            </div>
            <IconButton
              title="Свернуть загрузки"
              onClick={() => setUploadsOpen(false)}
            >
              <X size={18} />
            </IconButton>
          </div>
          <div className="overall-progress">
            <i
              style={{
                width: `${uploadTotal ? (uploaded / uploadTotal) * 100 : 0}%`,
              }}
            />
          </div>
          <div className="upload-items">
            {remoteUploads
              .filter(
                (t) =>
                  !queue.some(
                    (q) =>
                      q.serverId === t.id ||
                      q.upload?.url?.endsWith("/" + t.id),
                  ),
              )
              .map((t) => (
                <div className="upload-item" key={t.id}>
                  <div className="upload-file-icon">
                    <Smartphone size={20} />
                  </div>
                  <div className="upload-description">
                    <strong>{t.name}</strong>
                    <small>
                      {t.folder} · {bytes(Number(t.offset))} /{" "}
                      {bytes(Number(t.size))}
                    </small>
                    <div className="item-progress">
                      <i
                        style={{
                          width: `${t.size ? (Number(t.offset) / Number(t.size)) * 100 : 0}%`,
                        }}
                      />
                    </div>
                    <small>
                      {t.status === "duplicate"
                        ? "Ожидает решения о дубликате на устройстве"
                        : t.status === "uploaded"
                          ? "Ожидает подтверждения сохранения на устройстве"
                          : "Загрузка с другого устройства"}
                    </small>
                  </div>
                </div>
              ))}
            {queue.map((t) => (
              <div className="upload-item" key={t.id}>
                <div className="upload-file-icon">
                  {t.file.type.startsWith("video") ? (
                    <Film size={20} />
                  ) : t.file.type.startsWith("image") ? (
                    <Images size={20} />
                  ) : (
                    <FileText size={20} />
                  )}
                </div>
                <div className="upload-description">
                  <strong>{t.file.name}</strong>
                  <small>
                    {t.status === "waiting"
                      ? "В очереди"
                      : t.status === "paused"
                        ? "Приостановлено"
                        : t.status === "verifying"
                          ? "Проверяем и сохраняем оригинал…"
                          : t.status === "complete"
                            ? "Сохранён в " + (t.folder || "корень хранилища")
                            : t.status === "skipped"
                              ? "Дубликат пропущен"
                              : t.status === "cancelled"
                                ? "Отменено"
                                : t.status === "error"
                                  ? t.error
                                  : t.status === "duplicate"
                                    ? "Уже существует: " + t.duplicatePath
                                    : t.reconnecting
                                      ? "Восстанавливаем соединение. Продолжим с подтверждённого фрагмента…"
                                      : `${bytes(t.bytes)} / ${bytes(t.total)} · ${bytes(t.speed)}/с`}
                  </small>
                  {t.status === "uploading" && (
                    <div className="item-progress">
                      <i
                        style={{
                          width: `${t.total ? (t.bytes / t.total) * 100 : 0}%`,
                        }}
                      />
                    </div>
                  )}
                  {t.status === "duplicate" && (
                    <div className="duplicate-actions">
                      <button onClick={() => void uploadAction(t.id, "skip")}>
                        Пропустить
                      </button>
                      <button onClick={() => void uploadAction(t.id, "copy")}>
                        Сохранить копию
                      </button>
                      <button
                        onClick={() => void uploadAction(t.id, "skipAll")}
                      >
                        Пропускать все
                      </button>
                    </div>
                  )}
                </div>
                <div className="upload-controls">
                  {t.status === "uploading" ? (
                    <IconButton
                      title="Пауза"
                      onClick={() => void uploadAction(t.id, "pause")}
                    >
                      <Pause size={17} />
                    </IconButton>
                  ) : ["paused", "error"].includes(t.status) ? (
                    <IconButton
                      title="Продолжить загрузку"
                      onClick={() => void uploadAction(t.id, "resume")}
                    >
                      <Play size={17} />
                    </IconButton>
                  ) : ["complete", "skipped"].includes(t.status) ? (
                    <CheckCircle2 size={20} className="success" />
                  ) : null}
                  {[
                    "waiting",
                    "uploading",
                    "paused",
                    "error",
                    "duplicate",
                  ].includes(t.status) && (
                    <IconButton
                      title="Отменить загрузку"
                      onClick={() => void uploadAction(t.id, "cancel")}
                    >
                      <X size={16} />
                    </IconButton>
                  )}
                </div>
              </div>
            ))}
          </div>
          <div className="upload-foot">
            <span>
              <Wifi size={14} />
              Держите Safari открытым до завершения
            </span>
            <button onClick={clearFinished}>Очистить завершённые</button>
          </div>
        </div>
      )}
      {uploadDialog && (
        <Dialog
          title="Добавить в LocalCloud"
          onClose={() => setUploadDialog(false)}
        >
          <p className="dialog-subtitle">
            Оригиналы сохранятся на вашем компьютере.
          </p>
          <label className="field">
            Папка назначения
            <select
              value={uploadFolder}
              onChange={(e) => setUploadFolder(e.target.value)}
            >
              {[
                { path: "", name: "Корень хранилища" },
                ...(tree.length
                  ? tree
                  : [
                      { path: "Photos", name: "Photos" },
                      { path: "Videos", name: "Videos" },
                      { path: "Files", name: "Files" },
                    ]),
              ].map((f) => (
                <option key={f.path} value={f.path}>
                  {f.path || f.name}
                </option>
              ))}
            </select>
          </label>
          <div className="upload-choices">
            {settings?.local && (
              <button
                onClick={() => {
                  setUploadDialog(false);
                  directoryPicker.current?.click();
                }}
              >
                <span>
                  <Folder size={25} />
                </span>
                <div>
                  <strong>Импорт папки с ПК / USB</strong>
                  <small>С сохранением вложенных папок</small>
                </div>
                <ChevronRight size={19} />
              </button>
            )}
            <button
              onClick={() => {
                setUploadDialog(false);
                if (picker.current) {
                  picker.current.accept = "image/*";
                  picker.current.click();
                }
              }}
            >
              <span>
                <Images size={25} />
              </span>
              <div>
                <strong>Фотографии</strong>
                <small>JPEG, HEIC, PNG и другие</small>
              </div>
              <ChevronRight size={19} />
            </button>
            <button
              onClick={() => {
                setUploadDialog(false);
                if (picker.current) {
                  picker.current.accept = "video/*";
                  picker.current.click();
                }
              }}
            >
              <span>
                <Film size={25} />
              </span>
              <div>
                <strong>Видео</strong>
                <small>Оригинальное качество</small>
              </div>
              <ChevronRight size={19} />
            </button>
            <button
              onClick={() => {
                setUploadDialog(false);
                if (picker.current) {
                  picker.current.accept = "*/*";
                  picker.current.click();
                }
              }}
            >
              <span>
                <Folder size={25} />
              </span>
              <div>
                <strong>Любые файлы</strong>
                <small>Документы, архивы, всё остальное</small>
              </div>
              <ChevronRight size={19} />
            </button>
            <button
              onClick={() => {
                setUploadDialog(false);
                void openDialog("folder");
              }}
            >
              <span>
                <FolderPlus size={25} />
              </span>
              <div>
                <strong>Создать папку</strong>
                <small>Обычная папка на компьютере</small>
              </div>
              <ChevronRight size={19} />
            </button>
          </div>
          <p className="quiet-note">
            После перезапуска Safari выберите тот же файл и папку: незавершённая
            загрузка продолжится с подтверждённого места.
          </p>
        </Dialog>
      )}
      {dialog && (
        <Dialog
          title={
            {
              folder: "Новая папка",
              album: "Новый альбом",
              rename: "Переименовать файл",
              move: "Переместить файлы",
              copy: "Создать копию",
              trash: "Переместить в корзину?",
              purge: "Удалить навсегда?",
              restore: "Восстановить файлы",
              addAlbum: "Добавить в альбом",
              emptyTrash: "Очистить корзину?",
              deleteAlbum: "Удалить альбом?",
              renameAlbum: "Переименовать альбом",
              renameFolder: "Переименовать папку",
              deleteFolder: "Переместить папку в корзину?",
              moveFolder: "Переместить папку",
              copyFolder: "Копировать папку",
            }[dialog.type] || "Операция"
          }
          onClose={() => setDialog(null)}
        >
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void submitDialog();
            }}
          >
            {[
              "folder",
              "album",
              "rename",
              "renameAlbum",
              "renameFolder",
            ].includes(dialog.type) ? (
              <label className="field">
                Название
                <input
                  autoFocus
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  required
                  maxLength={220}
                />
              </label>
            ) : ["move", "copy", "moveFolder", "copyFolder"].includes(
                dialog.type,
              ) ? (
              <label className="field">
                Папка
                <select
                  value={target}
                  onChange={(e) => setTarget(e.target.value)}
                >
                  <option value="">Корень хранилища</option>
                  {tree.map((f) => (
                    <option key={f.path} value={f.path}>
                      {f.path}
                    </option>
                  ))}
                </select>
              </label>
            ) : dialog.type === "addAlbum" ? (
              <label className="field">
                Альбом
                <select
                  required
                  value={target}
                  onChange={(e) => setTarget(e.target.value)}
                >
                  <option value="">Выберите альбом</option>
                  {albums.map((a) => (
                    <option key={a.id} value={a.id}>
                      {a.name}
                    </option>
                  ))}
                </select>
                {!albums.length && (
                  <p>Сначала создайте альбом в разделе «Альбомы».</p>
                )}
              </label>
            ) : (
              <p className="dialog-subtitle">
                {["purge", "emptyTrash"].includes(dialog.type)
                  ? "Эти файлы будут удалены с диска. Восстановить их через LocalCloud не получится."
                  : dialog.type === "deleteAlbum"
                    ? "Будет удалена только коллекция. Все оригинальные файлы останутся на месте."
                    : dialog.type === "deleteFolder"
                      ? "Файлы будут перемещены в корзину с сохранением структуры папок для восстановления."
                      : "Файлы останутся в корзине, откуда их можно восстановить."}
              </p>
            )}
            <div className="dialog-actions">
              <button
                type="button"
                className="secondary"
                onClick={() => setDialog(null)}
              >
                Отмена
              </button>
              <button
                type="submit"
                className={
                  ["purge", "emptyTrash"].includes(dialog.type)
                    ? "primary destructive"
                    : "primary"
                }
              >
                Подтвердить
              </button>
            </div>
          </form>
        </Dialog>
      )}
      {phone && (
        <Dialog title="Ваше облако на iPhone" onClose={() => setPhone(false)}>
          <div className="phone-dialog">
            <div className="qr">
              <QRCodeSVG
                value={serverAddress}
                size={194}
                level="M"
                marginSize={2}
              />
            </div>
            <h3>Наведите камеру на QR-код</h3>
            {!!settings?.addresses.length && (
              <label className="field">
                Адрес компьютера
                <select
                  value={serverAddress}
                  onChange={(e) => setPhoneAddress(e.target.value)}
                >
                  {settings.addresses.map((address) => (
                    <option key={address} value={address}>
                      {address}
                    </option>
                  ))}
                </select>
              </label>
            )}
            <p>
              Телефон и компьютер должны быть подключены к одной домашней сети.
            </p>
            <div className="server-address">
              <code>{serverAddress}</code>
              <IconButton
                title="Копировать адрес"
                onClick={() => {
                  if (navigator.clipboard)
                    void navigator.clipboard
                      .writeText(serverAddress)
                      .then(() => notify("Адрес скопирован"));
                  else notify("Адрес: " + serverAddress);
                }}
              >
                <Copy size={17} />
              </IconButton>
            </div>
            {settings?.mdns && (
              <small>
                Также можно попробовать http://cloud.local:{settings.port}
              </small>
            )}
            <NetworkPanel local={settings?.local ?? false} />
            <div className="phone-hint">
              <Smartphone size={21} />
              <span>
                В Safari: «Поделиться» → «На экран Домой», чтобы открывать
                LocalCloud как приложение.
              </span>
            </div>
          </div>
        </Dialog>
      )}
      {context && (
        <div
          className="context-menu"
          role="menu"
          style={{ left: context.x, top: context.y }}
        >
          <strong>{context.file.name}</strong>
          {(context.file.trashed
            ? [
                {
                  name: "Восстановить",
                  icon: RotateCcw,
                  action: () => void act("restore", [context.file.id]),
                },
                {
                  name: "Удалить навсегда",
                  icon: Trash2,
                  action: () => void openDialog("purge", [context.file.id]),
                },
              ]
            : [
                {
                  name: "Открыть",
                  icon: ExternalLink,
                  action: () => setViewer(context.file),
                },
                {
                  name: "В избранное",
                  icon: Heart,
                  action: () => void act("favorite", [context.file.id]),
                },
                {
                  name: "Переименовать",
                  icon: FileText,
                  action: () =>
                    void openDialog(
                      "rename",
                      [context.file.id],
                      context.file.name,
                    ),
                },
                {
                  name: "Переместить",
                  icon: Move,
                  action: () => void openDialog("move", [context.file.id]),
                },
                {
                  name: "Копировать",
                  icon: Copy,
                  action: () => void openDialog("copy", [context.file.id]),
                },
                {
                  name: "Скачать оригинал",
                  icon: ArrowDownToLine,
                  action: () => {
                    location.href =
                      "/api/files/" +
                      context.file.id +
                      "/original?download=true";
                  },
                },
                {
                  name: "В корзину",
                  icon: Trash2,
                  action: () => void openDialog("trash", [context.file.id]),
                },
              ]
          ).map((item) => (
            <button role="menuitem" key={item.name} onClick={item.action}>
              <item.icon size={16} />
              {item.name}
            </button>
          ))}
        </div>
      )}
      {feature && (
        <Dialog
          title={
            feature === "tools" ? "Инструменты" : feature === "history"
              ? "История загрузок"
              : feature === "rename"
                ? "Массовое переименование"
                : "Умный альбом"
          }
          onClose={() => setFeature(null)}
        >
          {feature === "tools" ? (<div className="tools-layout"><NetworkPanel local={!!settings?.local}/>{settings?.local&&<><ProcessPanel/><BackupPanel/><OrganizePanel/><SharingPanel/></>}</div>) : feature === "history" ? (
            <><button className="secondary" onClick={()=>{setFeature(null);setUploadsOpen(true);}}>Активные загрузки</button><UploadHistory/></>
          ) : feature === "rename" ? (
            <BulkRename
              ids={[...selected]}
              onDone={() => {
                setFeature(null);
                setSelected(new Set());
                void load();
                notify("Файлы переименованы");
              }}
            />
          ) : (
            <form
              onSubmit={(e) => {
                e.preventDefault();
                void api("/collections", {
                  name: collectionName,
                  query: {
                    view: view === "albums" ? "photos" : view,
                    search: search || null,
                    folder: view === "files" ? folder : null,
                    extension: extension || null,
                    after: after || null,
                    before: before ? before + "T23:59:59Z" : null,
                    minSize: minSize ? Number(minSize) * 1048576 : null,
                    maxSize: maxSize ? Number(maxSize) * 1048576 : null,
                    sort,
                  },
                })
                  .then(() => {
                    setFeature(null);
                    setCollectionName("");
                    notify("Умный альбом сохранён");
                  })
                  .catch((e) => notify(e.message));
              }}
            >
              <label className="field">
                Название
                <input
                  required
                  value={collectionName}
                  onChange={(e) => setCollectionName(e.target.value)}
                />
              </label>
              <p className="quiet-note">
                Текущие фильтры сохранятся. Новые подходящие файлы появятся в
                коллекции автоматически.
              </p>
              <div className="dialog-actions">
                <button className="primary">Сохранить</button>
              </div>
            </form>
          )}
        </Dialog>
      )}
      {trackEdit && (
        <Dialog title="Редактировать трек" onClose={() => setTrackEdit(null)}>
          <TrackEditor
            file={trackEdit}
            onSaved={() => {
              setTrackEdit(null);
              void load();
              notify("Подписи трека сохранены");
            }}
          />
        </Dialog>
      )}
      {viewer && (
        <Viewer
          file={viewer}
          onTrackEdit={() => setTrackEdit(viewer)}
          onClose={() => setViewer(null)}
          onNavigate={(delta) => {
            const i = files.findIndex((f) => f.id === viewer.id);
            setViewer(files[(i + delta + files.length) % files.length]);
          }}
          onFavorite={() => {
            void act("favorite", [viewer.id]);
            setViewer({ ...viewer, favorite: !viewer.favorite });
          }}
          onDelete={() => {
            void openDialog("trash", [viewer.id]);
            setViewer(null);
          }}
          onMove={() => {
            void openDialog("move", [viewer.id]);
            setViewer(null);
          }}
          onAlbum={() => {
            void openDialog("addAlbum", [viewer.id]);
            setViewer(null);
          }}
        />
      )}
      {dragging && (
        <div className="drop-overlay">
          <Upload size={52} />
          <h2>Отпустите — мы сохраним</h2>
          <p>
            Загрузка в{" "}
            {view === "files" ? folder || "корень хранилища" : "Photos"}
          </p>
        </div>
      )}
      {toast && (
        <div className="toast" role="status">
          <CheckCircle2 size={19} />
          <span>{toast.text}</span>
          {toast.undo && (
            <button
              onClick={() => {
                toast.undo?.();
                setToast(null);
              }}
            >
              Отменить
            </button>
          )}
          <IconButton
            title="Закрыть уведомление"
            onClick={() => setToast(null)}
          >
            <X size={16} />
          </IconButton>
        </div>
      )}
    </div>
  );
}
function Empty({
  icon: Icon,
  title,
  text,
  action,
  onClick,
}: {
  icon: typeof Images;
  title: string;
  text: string;
  action?: string;
  onClick?: () => void;
}) {
  return (
    <div className="empty-state">
      <div className="empty-art">
        <div />
        <span>
          <Icon size={40} strokeWidth={1.35} />
        </span>
        <div />
      </div>
      <h2>{title}</h2>
      <p>{text}</p>
      {action && (
        <button className="primary" onClick={onClick}>
          <Plus size={17} />
          {action}
        </button>
      )}
    </div>
  );
}
function Unlock({ onUnlock }: { onUnlock: () => void }) {
  const [pin, setPin] = useState(""),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  return (
    <div className="startup">
      <div className="brand-mark large">
        <Cloud size={42} />
      </div>
      <h1>Ваше личное пространство</h1>
      <p>Введите PIN, установленный на компьютере.</p>
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          setBusy(true);
          try {
            await api("/unlock", { pin });
            onUnlock();
          } catch (e) {
            setError((e as Error).message);
          } finally {
            setBusy(false);
          }
        }}
      >
        <input
          autoFocus
          className="pin-input"
          type="password"
          inputMode="numeric"
          autoComplete="current-password"
          aria-label="PIN"
          value={pin}
          onChange={(e) => setPin(e.target.value)}
        />
        {error && <p className="danger">{error}</p>}
        <button className="primary" disabled={busy}>
          Открыть LocalCloud
        </button>
      </form>
    </div>
  );
}
function Setup({
  settings,
  onDone,
}: {
  settings: Settings | null;
  onDone: () => void;
}) {
  const [root, setRoot] = useState(settings?.storageRoot || ""),
    [lan, setLan] = useState(true),
    [startup, setStartup] = useState(true),
    [error, setError] = useState("");
  useEffect(() => {
    if (settings?.storageRoot) setRoot(settings.storageRoot);
  }, [settings]);
  return (
    <div className="onboarding">
      <div className="onboarding-art">
        <div className="orbit one" />
        <div className="orbit two" />
        <Cloud size={100} strokeWidth={1} />
        <span>
          Пусть ваши воспоминания
          <br />
          остаются дома.
        </span>
      </div>
      <div className="onboarding-content">
        <div className="eyebrow">LOCALCLOUD</div>
        <h1>
          Ваше облако.
          <br />
          На вашем компьютере.
        </h1>
        <p>
          Фото, видео и файлы в одном спокойном пространстве. Без подписок и
          внешних серверов.
        </p>
        {settings?.local ? (
          <form
            onSubmit={async (e) => {
              e.preventDefault();
              try {
                const response = await api<{ restartRequired: boolean }>(
                  "/setup",
                  {
                    storageRoot: root,
                    allowLan: lan,
                    launchAtStartup: startup,
                  },
                );
                if (response.restartRequired)
                  setError(
                    "Настройки сохранены. Перезапустите LocalCloud, чтобы открыть выбранное хранилище.",
                  );
                else onDone();
              } catch (e) {
                setError((e as Error).message);
              }
            }}
          >
            <label className="field">
              Папка хранения
              <input
                value={root}
                onChange={(e) => setRoot(e.target.value)}
                required
              />
            </label>
            <small className="quiet-note">
              Оригиналы будут обычными файлами в этой папке.
            </small>
            <label className="checkbox">
              <input
                type="checkbox"
                checked={lan}
                onChange={(e) => setLan(e.target.checked)}
              />
              Разрешить доступ из домашней сети
            </label>
            <label className="checkbox">
              <input
                type="checkbox"
                checked={startup}
                onChange={(e) => setStartup(e.target.checked)}
              />
              Запускать вместе с Windows
            </label>
            {error && <p className="danger">{error}</p>}
            <button className="primary">
              Начать
              <ArrowUpRight size={18} />
            </button>
          </form>
        ) : (
          <p>
            Завершите первый запуск на компьютере. После этого откройте этот
            адрес на телефоне.
          </p>
        )}
        <span className="onboarding-foot">
          <Lock size={13} />
          Ваши файлы не покидают домашнюю сеть
        </span>
      </div>
    </div>
  );
}
function StorageScreen({
  storage,
  settings,
  onPhone,
}: {
  storage: any;
  settings: Settings | null;
  onPhone: () => void;
}) {
  return (
    <div className="storage-screen">
      <div className="storage-main">
        <span className="storage-icon">
          <HardDrive size={30} />
        </span>
        <h2>Пространство для важного</h2>
        <p>Библиотека LocalCloud</p>
        <div className="storage-number">
          {storage ? bytes(storage.libraryBytes) : "—"}
        </div>
        <div className="storage-segments">
          {(storage?.groups || []).map((g: any) => (
            <i
              key={g.kind}
              className={"segment-" + g.kind}
              style={{
                width: `${Math.max(0.5, (g.bytes / storage.total) * 100)}%`,
              }}
            />
          ))}
        </div>
        <div className="storage-breakdown">
          {[
            { kind: "photo", name: "Фото", icon: Images },
            { kind: "video", name: "Видео", icon: Film },
            { kind: "audio", name: "Музыка", icon: Music2 },
            { kind: "file", name: "Файлы", icon: Folder },
          ].map((item) => {
            const g = storage?.groups.find((g: any) => g.kind === item.kind);
            return (
              <div key={item.kind}>
                <item.icon size={20} />
                <span>
                  {item.name}
                  <small>
                    {Number(g?.count || 0).toLocaleString("ru-RU")} файлов
                  </small>
                </span>
                <strong>{bytes(Number(g?.bytes || 0))}</strong>
              </div>
            );
          })}
        </div>
        <div className="disk-info">
          <span>Весь диск</span>
          <strong>{storage ? bytes(storage.total) : "—"}</strong>
          <span>Свободно</span>
          <strong>{storage ? bytes(storage.free) : "—"}</strong>
        </div>
        {settings?.storageRoot && (
          <code className="storage-path">{settings.storageRoot}</code>
        )}
      </div>
      <div className="storage-side">
        <div className="info-card">
          <Smartphone size={27} />
          <h3>Просто наведите камеру</h3>
          <p>
            Откройте LocalCloud на iPhone и переносите файлы по домашнему Wi-Fi.
          </p>
          <button className="secondary" onClick={onPhone}>
            Открыть QR-код
            <ArrowUpRight size={16} />
          </button>
        </div>
        <div className="info-card">
          <Lock size={26} />
          <h3>Оригиналы — ваши</h3>
          <p>
            LocalCloud сохраняет исходные файлы без перекодирования. Их всегда
            можно открыть в Проводнике.
          </p>
        </div>
        <div className="info-card backup-note">
          <Copy size={25} />
          <h3>Один диск — ещё не резервная копия</h3>
          <p>
            Проверяемая копия на второй диск или NAS доступна в настройках.
            Выберите отдельный носитель: ещё одна папка на том же диске не защищает от его отказа.
          </p>
        </div>
      </div>
    </div>
  );
}
function SettingsScreen({
  settings: s,
  theme,
  onTheme,
  notify,
  reload,
}: {
  settings: Settings | null;
  theme: string;
  onTheme: (t: string) => void;
  notify: (text: string) => void;
  reload: () => Promise<void>;
}) {
  const [port, setPort] = useState(s?.port || 43110),
    [lan, setLan] = useState(s?.allowLan ?? true),
    [count, setCount] = useState(s?.concurrentUploads || 2),
    [duplicate, setDuplicate] = useState(s?.duplicateBehavior || "ask"),
    [retention, setRetention] = useState(s?.trashRetentionDays || 30),
    [pin, setPin] = useState<string | null>(null),
    [busy, setBusy] = useState(false);
  if (!s) return null;
  async function save() {
    setBusy(true);
    try {
      const r = await api<{ restartRequired: boolean }>("/settings", {
        port,
        allowLan: lan,
        concurrentUploads: count,
        duplicateBehavior: duplicate,
        trashRetentionDays: retention,
        pin,
      });
      notify(
        r.restartRequired
          ? "Сохранено. Перезапустите LocalCloud для изменения сети."
          : "Настройки сохранены",
      );
      await reload();
    } catch (e) {
      notify((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="settings-screen">
      <section className="settings-section">
        <h2>
          <Sun size={20} />
          Внешний вид
        </h2>
        <div className="theme-options">
          {[
            { id: "light", name: "Светлая", icon: Sun },
            { id: "dark", name: "Тёмная", icon: Moon },
            { id: "system", name: "Системная", icon: Monitor },
          ].map((t) => (
            <button
              key={t.id}
              className={
                "theme-card " + t.id + (theme === t.id ? " selected-theme" : "")
              }
              onClick={() => onTheme(t.id)}
            >
              <span className="theme-preview">
                <i />
                <i />
                <i />
              </span>
              <span>
                <t.icon size={17} />
                {t.name}
                {theme === t.id && <Check size={16} />}
              </span>
            </button>
          ))}
        </div>
      </section>
      <section className="settings-section">
        <h2>
          <HardDrive size={20} />
          Хранилище
        </h2>
        <div className="setting-row">
          <div>
            <strong>Оригиналы на компьютере</strong>
            <p>{s.storageRoot || "Путь доступен на компьютере"}</p>
          </div>
          <span className="tag">Обычные файлы</span>
        </div>
        <div className="setting-row">
          <div>
            <strong>Проверить библиотеку</strong>
            <p>Найти изменения, сделанные в Проводнике.</p>
          </div>
          <button
            className="secondary"
            onClick={() =>
              void api("/rescan", {})
                .then(() => notify("Проверка библиотеки запущена"))
                .catch((e) => notify(e.message))
            }
          >
            <RefreshCw size={16} />
            Проверить
          </button>
        </div>
        <div className="setting-row">
          <div>
            <strong>Пересоздать превью</strong>
            <p>Оригинальные файлы останутся без изменений.</p>
          </div>
          <button
            className="secondary"
            onClick={() =>
              void api("/rebuild-thumbnails", {})
                .then(() => notify("Превью пересоздаются в фоне"))
                .catch((e) => notify(e.message))
            }
          >
            Пересоздать
          </button>
        </div>
      </section>
      <section className="settings-section">
        <h2>
          <Wifi size={20} />
          Сеть и безопасность
        </h2>
        <div className="setting-row">
          <div>
            <strong>Адреса в домашней сети</strong>
            <p>
              {s.addresses.join(" · ") ||
                "Локальный адрес: http://localhost:" + s.port}
            </p>
            <p>
              mDNS:{" "}
              {s.mdns
                ? "объявляется cloud.local:" + s.port
                : "используйте IP-адрес"}
            </p>
          </div>
        </div>
        <NetworkPanel local={s.local} />
        {s.local ? (
          <>
            <label className="setting-row">
              <div>
                <strong>Порт сервера</strong>
                <p>Вступает в силу после перезапуска.</p>
              </div>
              <input
                type="number"
                min={1024}
                max={65535}
                value={port}
                onChange={(e) => setPort(Number(e.target.value))}
              />
            </label>
            <label className="setting-row">
              <div>
                <strong>Доступ из домашней сети</strong>
                <p>Только локальные и частные адреса.</p>
              </div>
              <input
                type="checkbox"
                checked={lan}
                onChange={(e) => setLan(e.target.checked)}
              />
            </label>
            <label className="setting-row">
              <div>
                <strong>PIN для доступа</strong>
                <p>
                  {s.pinEnabled ? "Включён." : "Открытая домашняя сеть."}{" "}
                  Минимум 6 символов.
                </p>
              </div>
              <input
                type="password"
                autoComplete="new-password"
                placeholder="Не менять"
                value={pin ?? ""}
                onChange={(e) => setPin(e.target.value)}
              />
            </label>
            {s.pinEnabled && (
              <button
                className="text-button danger"
                onClick={() => {
                  setPin("");
                  notify("Нажмите «Сохранить настройки», чтобы отключить PIN");
                }}
              >
                Отключить PIN
              </button>
            )}
            <p className="quiet-note">
              HTTP в домашней сети не шифрует трафик. PIN ограничивает доступ,
              но не заменяет HTTPS. Пользуйтесь доверенной сетью.
            </p>
          </>
        ) : (
          <p className="quiet-note">
            Настройки сети и PIN изменяются на компьютере.
          </p>
        )}
      </section>
      <section className="settings-section">
        <h2>
          <Upload size={20} />
          Загрузки и корзина
        </h2>
        <label className="setting-row">
          <div>
            <strong>Одновременные загрузки</strong>
            <p>Две обычно дают хороший баланс скорости и нагрузки.</p>
          </div>
          <select
            disabled={!s.local}
            value={count}
            onChange={(e) => setCount(Number(e.target.value))}
          >
            {[1, 2, 3, 4].map((n) => (
              <option key={n}>{n}</option>
            ))}
          </select>
        </label>
        <label className="setting-row">
          <div>
            <strong>Одинаковые файлы</strong>
            <p>Проверка размера и SHA-256.</p>
          </div>
          <select
            disabled={!s.local}
            value={duplicate}
            onChange={(e) => setDuplicate(e.target.value)}
          >
            <option value="ask">Спрашивать</option>
            <option value="skip">Пропускать</option>
            <option value="copy">Сохранять копию</option>
          </select>
        </label>
        <label className="setting-row">
          <div>
            <strong>Хранение в корзине</strong>
            <p>После этого срока файлы удаляются с диска.</p>
          </div>
          <select
            disabled={!s.local}
            value={retention}
            onChange={(e) => setRetention(Number(e.target.value))}
          >
            {[7, 30, 60, 90, 365].map((n) => (
              <option key={n} value={n}>
                {n} дней
              </option>
            ))}
          </select>
        </label>
      </section>
      {s.local && (
        <>
          <OrganizePanel />
          <BackupPanel />
          <SharingPanel />
          <ProcessPanel />
        </>
      )}
      <section className="settings-section">
        <h2>
          <Info size={20} />О LocalCloud
        </h2>
        <div className="setting-row">
          <div>
            <strong>LocalCloud {s.version ?? "1.0.0"}</strong>
            <p>ASP.NET Core / React · данные только на вашем компьютере</p>
            <p>
              Обновление: локальный Update-пакет. Запустите
              Update-LocalCloud.cmd из распакованного архива; программа не
              проверяет обновления в интернете.
            </p>
            <p>
              Медиа-инструменты:{" "}
              {s.ffmpeg
                ? "FFmpeg найден"
                : "FFmpeg не установлен; часть форматов не имеет превью"}
            </p>
            <p>
              Автозапуск: {s.launchAtStartup ? "включён" : "выключен"} ·
              изменяется через Windows-приложение
            </p>
          </div>
        </div>
        <p className="quiet-note">
          Safari может приостановить передачу, когда экран заблокирован. Для
          больших файлов оставляйте приложение открытым. USB подключение само по
          себе не объединяет скорость с Wi-Fi.
        </p>
      </section>
      {s.local && (
        <button
          className="primary save-settings"
          disabled={busy}
          onClick={() => void save()}
        >
          {busy ? <Loader2 size={18} className="spin" /> : <Check size={18} />}
          Сохранить настройки
        </button>
      )}{" "}
      {s.pinEnabled && (
        <button
          className="secondary"
          onClick={() => void api("/logout", {}).then(() => location.reload())}
        >
          <LogOut size={17} />
          Заблокировать
        </button>
      )}
    </div>
  );
}
function Viewer({
  file: f,
  onTrackEdit,
  onClose,
  onNavigate,
  onFavorite,
  onDelete,
  onMove,
  onAlbum,
}: {
  file: FileEntry;
  onTrackEdit: () => void;
  onClose: () => void;
  onNavigate: (n: number) => void;
  onFavorite: () => void;
  onDelete: () => void;
  onMove: () => void;
  onAlbum: () => void;
}) {
  const [info, setInfo] = useState(false),
    [zoom, setZoom] = useState(1),
    [proxy, setProxy] = useState(""),
    [videoError, setVideoError] = useState(false),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [live, setLive] = useState(false);
  const music = useMusic();
  const [editing, setEditing] = useState(false),
    [recipe, setRecipe] = useState<Recipe>({ ...defaultRecipe }),
    [editedSrc, setEditedSrc] = useState("");
  const touch = useRef(0);
  useEffect(() => {
    setZoom(1);
    setEditing(false);
    setEditedSrc("");
    setRecipe({ ...defaultRecipe });
    if (f.kind === "photo")
      void api<Recipe>("/files/" + f.id + "/edit")
        .then((r) => {
          setRecipe(r);
          if (JSON.stringify(r) !== JSON.stringify(defaultRecipe))
            setEditedSrc("/api/files/" + f.id + "/edited");
        })
        .catch(() => {});
    setProxy("");
    setVideoError(false);
    setError("");
    setLive(false);
  }, [f.id]);
  let metadata: Record<string, string> = {};
  try {
    metadata = JSON.parse(f.metadata || "{}");
  } catch {}
  const src =
    f.kind === "photo"
      ? thumbnailUrl(f, 1024)
      : "/api/files/" + f.id + "/original";
  return (
    <div
      className={"viewer " + (info || editing ? "with-info" : "")}
      role="dialog"
      aria-modal="true"
      aria-label={f.name}
    >
      <header className="viewer-head">
        <div>
          <IconButton title="Закрыть просмотр" onClick={onClose}>
            <X size={23} />
          </IconButton>
          <span>
            {f.name}
            <small>{date(f.takenAt || f.modifiedAt)}</small>
          </span>
        </div>
        <div>
          <IconButton title="Избранное" onClick={onFavorite}>
            <Heart size={20} fill={f.favorite ? "currentColor" : "none"} />
          </IconButton>
          <a
            className="icon-button"
            aria-label="Скачать оригинал"
            href={"/api/files/" + f.id + "/original?download=true"}
          >
            <ArrowDownToLine size={21} />
          </a>
          <IconButton title="Переместить" onClick={onMove}>
            <Move size={20} />
          </IconButton>
          <IconButton title="Добавить в альбом" onClick={onAlbum}>
            <AlbumIcon size={20} />
          </IconButton>
          {f.kind === "photo" && (
            <IconButton
              title="Редактировать фото"
              onClick={() => {
                setEditing(!editing);
                setInfo(false);
                setZoom(1);
              }}
            >
              <SlidersHorizontal size={20} />
            </IconButton>
          )}
          {f.kind === "audio" && (
            <IconButton title="Редактировать трек" onClick={onTrackEdit}>
              <SlidersHorizontal size={20} />
            </IconButton>
          )}
          <IconButton title="Информация" onClick={() => setInfo(!info)}>
            <Info size={21} />
          </IconButton>
          <IconButton title="В корзину" onClick={onDelete}>
            <Trash2 size={20} />
          </IconButton>
        </div>
      </header>
      <div
        className="viewer-stage"
        onTouchStart={(e) => {
          if (f.kind === "photo" && !live) return;
          touch.current = (e.target as HTMLElement).closest(".media-controls")
            ? NaN
            : e.touches[0].clientX;
        }}
        onTouchEnd={(e) => {
          if (f.kind === "photo" && !live) return;
          const delta = e.changedTouches[0].clientX - touch.current;
          if (Math.abs(delta) > 70 && zoom === 1)
            onNavigate(delta < 0 ? 1 : -1);
        }}
      >
        <IconButton
          title="Предыдущий файл"
          className="viewer-prev"
          onClick={() => onNavigate(-1)}
        >
          <ChevronLeft size={30} />
        </IconButton>
        <div className="viewer-media">
          {f.kind === "video" || live ? (
            <>
              <MediaPlayer
                file={f}
                poster={thumbnailUrl(f, 1024)}
                autoPlay={live}
                src={
                  proxy ||
                  (live ? "/api/files/" + f.livePartner + "/original" : src)
                }
                onError={() => setVideoError(true)}
              />
              {videoError && (
                <div className="video-error">
                  <p>Браузер не может воспроизвести этот кодек.</p>
                  <button
                    className="secondary"
                    disabled={busy}
                    onClick={async () => {
                      setBusy(true);
                      try {
                        const p = await api<{ url: string }>(
                          "/files/" + (live ? f.livePartner : f.id) + "/proxy",
                          {},
                        );
                        setProxy(p.url);
                        setVideoError(false);
                      } catch (e) {
                        setError((e as Error).message);
                      } finally {
                        setBusy(false);
                      }
                    }}
                  >
                    {busy
                      ? "Создаём совместимое превью…"
                      : "Создать совместимое превью"}
                  </button>
                  {error && <p>{error}</p>}
                  <a href={"/api/files/" + f.id + "/original?download=true"}>
                    Скачать оригинал
                  </a>
                </div>
              )}
            </>
          ) : f.kind === "audio" ? (
            <div className="audio-detail">
              <Music2 size={60} />
              <h2>{musicTags(f).title}</h2>
              <p>
                {musicTags(f).artist} · {musicTags(f).album}
              </p>
              <button className="primary" onClick={() => music.play([f], 0)}>
                <Play size={18} />
                Воспроизвести
              </button>
              <button className="secondary" onClick={onTrackEdit}>
                Редактировать подписи и обложку
              </button>
              <small>Музыка продолжит играть после закрытия просмотра.</small>
            </div>
          ) : f.kind === "photo" ? (
            <PhotoCanvas
              src={editing ? src : editedSrc || src}
              alt={f.name}
              zoom={zoom}
              onZoom={setZoom}
              onNavigate={onNavigate}
              rotation={editing ? recipe.rotation : 0}
              exposure={editing ? recipe.exposure : 0}
              crop={editing ? recipe : undefined}
            />
          ) : f.extension === "pdf" ? (
            <React.Suspense fallback={<Loader2 className="spinner" />}>
              <PdfViewer file={f} />
            </React.Suspense>
          ) : (
            <div className="viewer-document">
              <FileText size={70} />
              <h2>{f.name}</h2>
              <a
                className="primary"
                href={"/api/files/" + f.id + "/original?download=true"}
              >
                Скачать файл
              </a>
            </div>
          )}
        </div>
        <IconButton
          title="Следующий файл"
          className="viewer-next"
          onClick={() => onNavigate(1)}
        >
          <ChevronRight size={30} />
        </IconButton>
      </div>
      {f.kind === "photo" && (
        <div className="viewer-zoom">
          <IconButton
            title="Уменьшить"
            onClick={() => setZoom(Math.max(1, zoom - 0.5))}
          >
            <ZoomOut size={18} />
          </IconButton>
          <span>{Math.round(zoom * 100)}%</span>
          <IconButton
            title="Увеличить"
            onClick={() => setZoom(Math.min(6, zoom + 0.5))}
          >
            <ZoomIn size={18} />
          </IconButton>
          {f.livePartner && (
            <button className="text-button" onClick={() => setLive(!live)}>
              <Play size={15} />
              {live ? "Фото" : "Live Photo"}
            </button>
          )}
        </div>
      )}
      {editing && (
        <PhotoEditPanel
          file={f}
          recipe={recipe}
          onChange={setRecipe}
          onSaved={() => {
            setEditing(false);
            setEditedSrc("/api/files/" + f.id + "/edited?v=" + Date.now());
          }}
        />
      )}
      {info && (
        <aside className="info-panel">
          <h2>Информация</h2>
          <div className="info-filename">
            <FileText size={23} />
            <span>{f.name}</span>
          </div>
          <dl>
            <dt>Папка</dt>
            <dd>{f.path}</dd>
            <dt>Размер</dt>
            <dd>{bytes(f.size)}</dd>
            <dt>Разрешение</dt>
            <dd>
              {f.width && f.height
                ? `${f.width} × ${f.height}`
                : "Не определено"}
            </dd>
            {f.duration != null && (
              <>
                <dt>Длительность</dt>
                <dd>{duration(f.duration)}</dd>
              </>
            )}
            <dt>Дата</dt>
            <dd>{date(f.takenAt || f.modifiedAt)}</dd>
            <dt>Оригинал</dt>
            <dd>{f.hash ? "Проверен SHA-256" : "Индексируется"}</dd>
            {f.livePartner && (
              <>
                <dt>Live Photo</dt>
                <dd>
                  {metadata.LivePairMethod === "metadata"
                    ? "Подтверждённая пара по Apple Content Identifier"
                    : "Предполагаемая пара по имени файла"}
                </dd>
              </>
            )}
          </dl>
          {Object.entries(metadata)
            .filter(
              ([key]) => !key.includes("Maker") && !key.includes("Thumbnail"),
            )
            .slice(0, 50)
            .map(([key, value]) => (
              <div className="metadata-row" key={key}>
                <small>{key}</small>
                <p>{String(value)}</p>
              </div>
            ))}
        </aside>
      )}
    </div>
  );
}
createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    {location.pathname.startsWith("/s/") ? (
      <SharedLibrary />
    ) : (
      <MusicProvider>
        <App />
      </MusicProvider>
    )}
  </React.StrictMode>,
);
if ("serviceWorker" in navigator && window.isSecureContext)
  void navigator.serviceWorker.register("/sw.js");
