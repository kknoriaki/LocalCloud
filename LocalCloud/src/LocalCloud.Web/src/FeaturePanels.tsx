import { useEffect, useState } from "react";
import {
  HardDrive,
  Users,
  History,
  Loader2,
  RotateCw,
  Check,
  Music2,
  FolderSync,
  Trash2,
} from "lucide-react";
import { api, bytes, date, thumbnailUrl, type FileEntry } from "./api";
import { musicTags } from "./Music";
export function BackupPanel() {
  const [s, setS] = useState<any>(null),
    [target, setTarget] = useState(""),
    [error, setError] = useState("");
  async function refresh() {
    try {
      const n = await api<any>("/backup");
      setS(n);
      setTarget((t) => t || n.target || "");
    } catch (e) {
      setError((e as Error).message);
    }
  }
  useEffect(() => {
    void refresh();
    const t = setInterval(() => void refresh(), 2500);
    return () => clearInterval(t);
  }, []);
  return (
    <section className="settings-section">
      <h2>
        <HardDrive size={20} />
        Вторая копия
      </h2>
      <p className="quiet-note">
        Оригиналы копируются в обычные папки на втором диске или NAS. Каждый
        файл проверяется SHA-256. Удаление из библиотеки не удаляет резервную
        копию.
      </p>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setError("");
          void api("/backup", { target })
            .then(refresh)
            .catch((e) => setError(e.message));
        }}
      >
        <label className="field">
          Папка резервной копии
          <input
            required
            value={target}
            disabled={s?.phase === "running"}
            placeholder="F:\LocalCloudBackup или \\NAS\Photos"
            onChange={(e) => setTarget(e.target.value)}
          />
        </label>
        <div className="feature-actions">
          <button className="primary" disabled={s?.phase === "running"}>
            <FolderSync size={17} />
            Создать и проверить копию
          </button>
          {s?.phase === "running" && (
            <button
              className="secondary"
              type="button"
              onClick={() => void api("/backup/cancel", {}).then(refresh)}
            >
              Остановить
            </button>
          )}
        </div>
      </form>
      {s && (
        <div className="backup-progress" role="status">
          <strong>
            {s.phase === "running"
              ? "Копируем и проверяем"
              : s.phase === "complete"
                ? "Копия проверена"
                : s.phase === "cancelled"
                  ? "Копирование остановлено"
                  : s.phase === "failed"
                    ? "Копирование не завершено"
                    : "Готово к резервному копированию"}
          </strong>
          <p>
            По последней проверке SHA-256: {s.verified} файлов
            {s.total > 0
              ? ` · ${s.completed} / ${s.total} · ${bytes(s.bytes)}`
              : ""}
          </p>
          {s.current && <small>{s.current}</small>}
          {s.phase === "running" && (
            <progress max={Math.max(s.total, 1)} value={s.completed} />
          )}
        </div>
      )}
      {(error || s?.error) && <p className="danger">{error || s.error}</p>}
    </section>
  );
}
export function SharingPanel() {
  const [s, setS] = useState<any>(null),
    [folder, setFolder] = useState("Photos/"),
    [name, setName] = useState("Фото для друзей"),
    [days, setDays] = useState(30),
    [download, setDownload] = useState(true),
    [link, setLink] = useState(""),
    [error, setError] = useState("");
  async function refresh() {
    try {
      setS(await api("/shares"));
    } catch (e) {
      setError((e as Error).message);
    }
  }
  useEffect(() => {
    void refresh();
  }, []);
  return (
    <section className="settings-section">
      <h2>
        <Users size={20} />
        Доступ друзьям
      </h2>
      <p className="quiet-note">
        Ссылка открывает выбранную папку и вложенные файлы только для просмотра.
        Друзья не могут загружать, удалять или открывать другие папки. Любой, у
        кого есть ссылка, имеет этот доступ до отзыва или истечения срока.
      </p>
      <label className="setting-row">
        <div>
          <strong>Доступ через Radmin VPN</strong>
          <p>
            Отдельный режим для сети 26.x.x.x. Общая библиотека через VPN
            закрыта.
          </p>
        </div>
        <input
          type="checkbox"
          checked={!!s?.enabled}
          onChange={(e) =>
            void api("/shares/vpn", { enabled: e.target.checked })
              .then(refresh)
              .catch((e) => setError(e.message))
          }
        />
      </label>
      {s?.enabled && (
        <p className="quiet-note">
          {s.addresses.length
            ? `Ваш адрес Radmin: ${s.addresses.join(" · ")}`
            : "Radmin VPN не обнаружен. Подключите ПК к сети Radmin, затем обновите эту страницу."}{" "}
          Разрешите порт LocalCloud для адаптера Radmin через кнопку ниже.
        </p>
      )}
      {s?.enabled && (
        <button
          className="secondary"
          onClick={() =>
            void api("/network/radmin", {})
              .then(() =>
                setError(
                  "Подтвердите запрос Windows. Затем откройте ссылку с компьютера друга.",
                ),
              )
              .catch((e) => setError(e.message))
          }
        >
          Разрешить адаптер Radmin в Windows
        </button>
      )}
      <form
        className="feature-form"
        onSubmit={(e) => {
          e.preventDefault();
          setError("");
          void api<any>("/shares", { folder, name, download, days })
            .then((n) => {
              setLink(
                (s?.enabled && s.addresses[0]
                  ? s.addresses[0]
                  : s?.lanAddresses[0] || location.origin) + n.path,
              );
              void refresh();
            })
            .catch((e) => setError(e.message));
        }}
      >
        <label className="field">
          Папка внутри библиотеки
          <input
            required
            value={folder}
            onChange={(e) => setFolder(e.target.value)}
            placeholder="Photos/Вечеринка"
          />
        </label>
        <label className="field">
          Название для друзей
          <input
            required
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
        </label>
        <div className="feature-actions">
          <label>
            Срок{" "}
            <select
              value={days}
              onChange={(e) => setDays(Number(e.target.value))}
            >
              {[1, 7, 30, 90].map((n) => (
                <option key={n} value={n}>
                  {n} дней
                </option>
              ))}
            </select>
          </label>
          <label className="checkbox">
            <input
              type="checkbox"
              checked={download}
              onChange={(e) => setDownload(e.target.checked)}
            />
            Кнопка скачивания оригиналов
          </label>
        </div>
        <button className="primary">Создать ссылку</button>
      </form>
      {link && (
        <label className="field">
          Скопируйте и отправьте друзьям
          <input readOnly value={link} onFocus={(e) => e.target.select()} />
          <small>
            Для Radmin друг должен состоять в вашей сети. Фото и воспроизводимое
            видео всё равно доступны браузеру для просмотра.
          </small>
        </label>
      )}
      {s?.items.map((item: any) => (
        <div className="shared-row" key={item.id}>
          <div>
            <strong>{item.name}</strong>
            <small>
              {item.folder} · до {date(item.expires)}
            </small>
          </div>
          <button
            className="text-button danger"
            onClick={() => {
              if (confirm("Отозвать эту ссылку?"))
                void api("/shares/" + item.id, undefined, "DELETE").then(
                  refresh,
                );
            }}
          >
            Отозвать
          </button>
        </div>
      ))}
      {error && (
        <p role="status" className="quiet-note">
          {error}
        </p>
      )}
    </section>
  );
}
export function UploadHistory() {
  const [rows, setRows] = useState<any[]>([]),
    [offset, setOffset] = useState(0),
    [error, setError] = useState("");
  useEffect(() => {
    void api<any[]>("/uploads/history?offset=" + offset)
      .then(setRows)
      .catch((e) => setError(e.message));
  }, [offset]);
  const labels: Record<string, string> = {
    complete: "Сохранён и проверен",
    skipped: "Дубликат пропущен",
    cancelled: "Отменён",
    duplicate: "Нужно выбрать действие",
    uploaded: "Передан, ожидает проверки",
    finalizing: "Завершаем сохранение",
    uploading: "Выберите исходный файл, чтобы продолжить",
  };
  return (
    <div className="upload-history">
      {!rows.length && !error && (
        <p className="quiet-note">
          Здесь появятся реальные результаты загрузок.
        </p>
      )}
      {rows.map((r) => (
        <div className="history-row" key={r.id}>
          <History size={20} />
          <div>
            <strong>{r.name}</strong>
            <small>
              {labels[r.status] || r.status} · {bytes(r.size)}
            </small>
            <small>
              {r.savedPath || r.duplicatePath || r.folder} · {date(r.createdAt)}
            </small>
            {r.hash && <small>SHA-256 проверен</small>}
          </div>
        </div>
      ))}
      {error && <p className="danger">{error}</p>}
      <div className="feature-actions">
        <button
          className="secondary"
          disabled={!offset}
          onClick={() => setOffset(Math.max(0, offset - 60))}
        >
          Ранее
        </button>
        <button
          className="secondary"
          disabled={rows.length < 60}
          onClick={() => setOffset(offset + 60)}
        >
          Следующие
        </button>
      </div>
    </div>
  );
}
export function BulkRename({
  ids,
  onDone,
}: {
  ids: string[];
  onDone: () => void;
}) {
  const [prefix, setPrefix] = useState("Фото "),
    [start, setStart] = useState(1),
    [plans, setPlans] = useState<any[]>([]),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  useEffect(() => {
    const t = setTimeout(
      () =>
        void api<any[]>("/files/rename-plan", { ids, prefix, start })
          .then((n) => {
            setPlans(n);
            setError("");
          })
          .catch((e) => {
            setError(e.message);
            setPlans([]);
          }),
      250,
    );
    return () => clearTimeout(t);
  }, [ids.join(","), prefix, start]);
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        setBusy(true);
        void api("/files/rename-batch", { ids, prefix, start })
          .then(onDone)
          .catch((e) => setError(e.message))
          .finally(() => setBusy(false));
      }}
    >
      <p className="quiet-note">
        Расширения и содержимое файлов сохранятся. Альбомы и избранное остаются
        привязаны к тем же файлам.
      </p>
      <label className="field">
        Начало имени
        <input
          value={prefix}
          onChange={(e) => setPrefix(e.target.value)}
          required
        />
      </label>
      <label className="field">
        Первый номер
        <input
          type="number"
          min={0}
          max={1000000}
          value={start}
          onChange={(e) => setStart(Number(e.target.value))}
        />
      </label>
      <div className="rename-preview">
        {plans.slice(0, 20).map((p) => (
          <div key={p.id}>
            <small>{p.oldName}</small>
            <strong>{p.name}</strong>
          </div>
        ))}
      </div>
      {error && <p className="danger">{error}</p>}
      <div className="dialog-actions">
        <button className="primary" disabled={busy || !plans.length}>
          {busy ? (
            <Loader2 className="spinner" size={17} />
          ) : (
            <Check size={17} />
          )}
          Переименовать {ids.length}
        </button>
      </div>
    </form>
  );
}
export type Recipe = {
  rotation: number;
  exposure: number;
  x: number;
  y: number;
  width: number;
  height: number;
};
export const defaultRecipe: Recipe = {
  rotation: 0,
  exposure: 0,
  x: 0,
  y: 0,
  width: 1,
  height: 1,
};
export function PhotoEditPanel({
  file,
  recipe,
  onChange,
  onSaved,
}: {
  file: FileEntry;
  recipe: Recipe;
  onChange: (r: Recipe) => void;
  onSaved: () => void;
}) {
  const [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  return (
    <aside className="photo-edit-panel">
      <h2>Редактировать фото</h2>
      <p className="quiet-note">
        Оригинал остаётся неизменным. Экспорт создаёт отдельный JPEG.
      </p>
      <button
        className="secondary"
        onClick={() =>
          onChange({ ...recipe, rotation: (recipe.rotation + 90) % 360 })
        }
      >
        <RotateCw size={18} />
        Повернуть
      </button>
      <label className="field">
        Экспозиция · {recipe.exposure.toFixed(1)} EV
        <input
          type="range"
          min={-2}
          max={2}
          step={0.1}
          value={recipe.exposure}
          onChange={(e) =>
            onChange({ ...recipe, exposure: Number(e.target.value) })
          }
        />
      </label>
      <label className="field">
        Кадрирование
        <select
          onChange={(e) => {
            if (e.target.value === "full")
              onChange({ ...recipe, x: 0, y: 0, width: 1, height: 1 });
            else {
              const w = file.width || 1,
                h = file.height || 1,
                rw = Math.min(1, h / w),
                rh = Math.min(1, w / h);
              onChange({
                ...recipe,
                x: (1 - rw) / 2,
                y: (1 - rh) / 2,
                width: rw,
                height: rh,
              });
            }
          }}
        >
          <option value="full">Весь снимок</option>
          <option value="square">Квадрат по центру</option>
        </select>
      </label>
      <div className="crop-fields">
        {(["x", "y", "width", "height"] as const).map((k) => (
          <label key={k}>
            {{ x: "Слева", y: "Сверху", width: "Ширина", height: "Высота" }[k]}{" "}
            %
            <input
              type="number"
              min={k === "width" || k === "height" ? 1 : 0}
              max={100}
              value={Math.round(recipe[k] * 100)}
              onChange={(e) =>
                onChange({ ...recipe, [k]: Number(e.target.value) / 100 })
              }
            />
          </label>
        ))}
      </div>
      <div className="feature-actions">
        <button
          className="primary"
          disabled={busy}
          onClick={() => {
            setBusy(true);
            void api("/files/" + file.id + "/edit", recipe)
              .then(onSaved)
              .catch((e) => setError(e.message))
              .finally(() => setBusy(false));
          }}
        >
          Сохранить изменения
        </button>
        <button
          className="secondary"
          onClick={() => onChange({ ...defaultRecipe })}
        >
          Сбросить
        </button>
      </div>
      <a
        className="text-button"
        href={"/api/files/" + file.id + "/edited?export=true"}
      >
        Экспортировать сохранённый вариант
      </a>
      {error && <p className="danger">{error}</p>}
    </aside>
  );
}
export function TrackEditor({
  file,
  onSaved,
}: {
  file: FileEntry;
  onSaved: () => void;
}) {
  const t = musicTags(file),
    [title, setTitle] = useState(t.title),
    [artist, setArtist] = useState(t.artist),
    [album, setAlbum] = useState(t.album),
    [cover, setCover] = useState<string>(() => {
      try {
        return JSON.parse(file.metadata || "{}").coverId || "";
      } catch {
        return "";
      }
    }),
    [photos, setPhotos] = useState<FileEntry[]>([]),
    [search, setSearch] = useState(""),
    [error, setError] = useState(""),
    [preview,setPreview]=useState<any>(null),
    [writing,setWriting]=useState(false),
    [receipt,setReceipt]=useState<any>(null);
  useEffect(()=>{setPreview(null);setReceipt(null);},[title,artist,album,cover]);
  useEffect(() => {
    const timer = setTimeout(
      () =>
        void api<{ items: FileEntry[] }>(
          "/files?view=photos&limit=60&search=" + encodeURIComponent(search),
        )
          .then((n) => setPhotos(n.items))
          .catch((e) => setError(e.message)),
      200,
    );
    return () => clearTimeout(timer);
  }, [search]);
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        void api("/files/" + file.id + "/audio-tags", {
          title,
          artist,
          album,
          coverId: cover || null,
        })
          .then(() => {
            window.dispatchEvent(new Event("lc-track-changed"));
            onSaved();
          })
          .catch((e) => setError(e.message));
      }}
    >
      <p className="quiet-note">
        Подписи и обложка сохраняются в LocalCloud. Сам аудиофайл не меняется.
      </p>
      {[
        ["Название", title, setTitle],
        ["Исполнитель", artist, setArtist],
        ["Альбом", album, setAlbum],
      ].map(([label, value, set]) => (
        <label className="field" key={String(label)}>
          {String(label)}
          <input
            maxLength={250}
            value={String(value)}
            onChange={(e) => (set as (s: string) => void)(e.target.value)}
          />
        </label>
      ))}
      <label className="field">
        Обложка из медиатеки
        <input
          placeholder="Найти фото"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </label>
      <div className="cover-picker">
        <button
          type="button"
          className={!cover ? "chosen" : ""}
          onClick={() => setCover("")}
        >
          <Music2 />
          Исходная
        </button>
        {photos.map((p) => (
          <button
            type="button"
            aria-label={p.name}
            className={cover === p.id ? "chosen" : ""}
            key={p.id}
            onClick={() => setCover(p.id)}
          >
            <img src={thumbnailUrl(p, 256)} alt={p.name} />
          </button>
        ))}
      </div>
      <div className="dialog-actions">
        <button className="primary" disabled={writing}>Сохранить подписи</button>
        {file.extension==="mp3"&&<button type="button" className="secondary" disabled={writing} onClick={async()=>{setWriting(true);setError("");try{setPreview(await api("/files/"+file.id+"/audio-tags/preview",{title,artist,album,coverId:cover||null}));}catch(e){setError((e as Error).message);}finally{setWriting(false);}}}>Предпросмотр записи в MP3</button>}
      </div>
      {preview&&<section className="tag-write-preview"><h3>Запись в оригинальный MP3</h3><table><thead><tr><th>Тег</th><th>Сейчас</th><th>Будет</th></tr></thead><tbody>{[["Название","title"],["Исполнитель","artist"],["Альбом","album"]].map(([label,key])=><tr key={key}><td>{label}</td><td>{preview.before?.[key]||"—"}</td><td>{preview.after?.[key[0].toUpperCase()+key.slice(1)]||preview.after?.[key]||"—"}</td></tr>)}</tbody></table><p>Обложка: {cover ? photos.find(p=>p.id===cover)?.name || "выбранное фото" : "сохранить встроенную"}.</p><p>{preview.message}</p><p className="quiet-note">Резервная копия: {preview.backup}</p><button type="button" className="primary" disabled={writing} onClick={async()=>{setWriting(true);setError("");try{setReceipt(await api("/files/"+file.id+"/audio-tags/write",{token:preview.token,confirmed:true}));setPreview(null);window.dispatchEvent(new Event("lc-track-changed"));window.dispatchEvent(new Event("lc-refresh"));}catch(e){setError((e as Error).message);setPreview(null);}finally{setWriting(false);}}}>{writing?"Проверяем и записываем…":"Подтверждаю: создать backup и записать теги"}</button></section>}
      {receipt&&<p role="status">Теги записаны. Аудиоданные проверены. Оригинальная копия: {receipt.backup}</p>}
      {error && <p className="danger">{error}</p>}
    </form>
  );
}
export function Collections({ onOpen }: { onOpen: (q: any) => void }) {
  const [rows, setRows] = useState<any[]>([]);
  function refresh() {
    void api<any[]>("/collections").then(setRows);
  }
  useEffect(refresh, []);
  return rows.length ? (
    <section className="smart-collections">
      <h3>Умные альбомы</h3>
      <p className="quiet-note">
        Состав обновляется автоматически по сохранённым условиям.
      </p>
      {rows.map((r) => (
        <div className="shared-row" key={r.id}>
          <button
            className="secondary"
            onClick={() => onOpen(JSON.parse(r.query))}
          >
            {r.name}
          </button>
          <button
            aria-label={"Удалить " + r.name}
            className="icon-button"
            onClick={() => {
              if (confirm("Удалить условие коллекции? Файлы останутся."))
                void api("/collections/" + r.id, undefined, "DELETE").then(
                  refresh,
                );
            }}
          >
            <Trash2 size={16} />
          </button>
        </div>
      ))}
    </section>
  ) : null;
}

export function OrganizePanel() {
  const [plans, setPlans] = useState<any[] | null>(null),
    [busy, setBusy] = useState(false),
    [message, setMessage] = useState("");
  return (
    <section className="settings-section">
      <h2>
        <FolderSync size={20} />
        Порядок в стандартных папках
      </h2>
      <p className="quiet-note">
        Только файлы непосредственно в Photos, Videos, Music и Files будут
        распределены по типу. Вложенные и пользовательские папки, альбомы и пары
        Live Photo сохраняются.
      </p>
      <button
        className="secondary"
        disabled={busy}
        onClick={() =>
          void api<any[]>("/organize")
            .then(setPlans)
            .catch((e) => setMessage(e.message))
        }
      >
        Показать перемещения
      </button>
      {plans && (
        <>
          <div className="rename-preview">
            {plans.slice(0, 50).map((p) => (
              <div key={p.id}>
                <small>{p.path}</small>
                <strong>→ {p.folder}</strong>
              </div>
            ))}
          </div>
          <p>
            {plans.length
              ? plans.length + " файлов к перемещению"
              : "Стандартные папки уже в порядке."}
          </p>
          {plans.length > 0 && (
            <button
              className="primary"
              disabled={busy}
              onClick={() => {
                setBusy(true);
                void api<any>("/organize", {})
                  .then((n) => {
                    setMessage("Перемещено файлов: " + n.count);
                    setPlans(null);
                    window.dispatchEvent(new Event("lc-refresh"));
                  })
                  .catch((e) => setMessage(e.message))
                  .finally(() => setBusy(false));
              }}
            >
              Переместить {plans.length} файлов
            </button>
          )}
        </>
      )}
      {message && <p role="status">{message}</p>}
    </section>
  );
}
export function ProcessPanel() {
  const [data, setData] = useState<any>(null),[background,setBackground]=useState<any>(null),[error,setError]=useState("");
  useEffect(() => {
    let alive=true,inFlight=false;
    const refresh=async()=>{if(document.hidden||inFlight)return;inFlight=true;try{const[p,b]=await Promise.all([api("/processes"),api("/background")]);if(alive){setData(p);setBackground(b);setError("");}}catch(e){if(alive)setError((e as Error).message);}finally{inFlight=false;}};
    void refresh();const timer=setInterval(()=>void refresh(),5000);return()=>{alive=false;clearInterval(timer);};
  }, []);
  return data ? (
    <section className="settings-section">
      <h2>Нагрузка и фоновые задачи</h2>
      {background&&<><div className="server-metrics"><div><strong>{background.cpuPercent}%</strong><span>CPU сервера</span></div><div><strong>{Math.round(background.memoryBytes/1048576)} МБ</strong><span>Память сервера</span></div><div><strong>{background.mediaProcesses.length}</strong><span>Медиапроцессы</span></div></div><p role="status"><strong>{background.task}</strong>{background.file&&" · "+background.file}</p><button className="secondary" onClick={async()=>{try{setBackground(await api("/background",{paused:!background.paused}));}catch(e){setError((e as Error).message);}}}>{background.paused?"Продолжить обработку":"Приостановить фоновые задачи"}</button><p className="quiet-note">Загрузки и воспроизведение продолжаются. Метрики относятся к серверу LocalCloud. Пауза применяется после текущего файла.</p>{background.mediaProcesses.map((p:any)=><p key={p.pid}>{p.name} · PID {p.pid}</p>)}</>}
      {error&&<p className="danger">{error}</p>}
      <p className="quiet-note">
        В диспетчере задач используйте вкладку «Подробности». Сначала закрывайте
        LocalCloud через трей → Выход.
      </p>
      <dl className="process-info">
        <dt>Приложение</dt>
        <dd>{data.application}</dd>
        <dt>Сервер</dt>
        <dd>
          {data.server.name} · PID {data.server.pid}
        </dd>
        <dt>Путь сервера</dt>
        <dd>{data.server.path}</dd>
        <dt>Библиотека</dt>
        <dd>{data.storage}</dd>
        <dt>Подключение</dt>
        <dd>
          {data.lan ? "Домашняя сеть" : "Только этот ПК"} · порт {data.port}
        </dd>
      </dl>
      <p className="quiet-note">
        LocalServiceNoNetworkFirewall, LocalServiceHttp и Local Security
        Authority Process — службы Windows. Они не относятся к LocalCloud.
      </p>
    </section>
  ) : null;
}

