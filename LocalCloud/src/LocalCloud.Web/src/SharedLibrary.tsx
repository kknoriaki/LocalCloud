import { useEffect, useState } from "react";
import {
  Cloud,
  FileText,
  Music2,
  Film,
  X,
  ArrowDownToLine,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import { bytes } from "./api";
export function SharedLibrary() {
  const token = location.pathname.split("/")[2],
    [data, setData] = useState<any>(null),
    [offset, setOffset] = useState(0),
    [viewer, setViewer] = useState<any>(null),
    [error, setError] = useState("");
  useEffect(() => {
    let alive = true;
    void fetch("/api/shared/" + token + "?offset=" + offset)
      .then(async (r) => {
        if (!r.ok)
          throw new Error(
            "Ссылка недоступна, отозвана или срок её действия истёк.",
          );
        return r.json();
      })
      .then((n) => {
        if (alive) setData(n);
      })
      .catch((e) => {
        if (alive) setError(e.message);
      });
    return () => {
      alive = false;
    };
  }, [token, offset]);
  const path = (f: any, action: string) =>
    "/api/shared/" + token + "/files/" + f.id + "/" + action;
  return (
    <main className="shared-library">
      <header>
        <div className="brand">
          <Cloud size={28} />
          LocalCloud
        </div>
        <small>Папка, которой с вами поделились</small>
      </header>
      <h1>{data?.name || "Общая папка"}</h1>
      <p className="quiet-note">
        {data
          ? data.total + " файлов · доступ только для просмотра"
          : "Открываем папку…"}
      </p>
      {error && <p role="alert">{error}</p>}
      <div className="shared-gallery">
        {data?.items.map((f: any) => (
          <button
            className="shared-file"
            key={f.id}
            onClick={() => setViewer(f)}
          >
            <div>
              {f.kind === "photo" || f.kind === "video" ? (
                <img
                  src={path(f, "thumbnail")}
                  alt=""
                  loading="lazy"
                  onError={(e) => {
                    e.currentTarget.style.visibility = "hidden";
                  }}
                />
              ) : f.kind === "audio" ? (
                <Music2 size={40} />
              ) : (
                <FileText size={40} />
              )}
            </div>
            <strong>{f.name}</strong>
            <small>
              {bytes(f.size)}
              {f.kind === "video" ? " · Видео" : ""}
            </small>
          </button>
        ))}
      </div>
      <div className="feature-actions">
        <button
          className="secondary"
          disabled={!offset}
          onClick={() => setOffset(Math.max(0, offset - 60))}
        >
          Назад
        </button>
        <button
          className="secondary"
          disabled={!data || offset + 60 >= data.total}
          onClick={() => setOffset(offset + 60)}
        >
          Далее
        </button>
      </div>
      {viewer && (
        <div
          className="shared-viewer"
          role="dialog"
          aria-modal="true"
          aria-label={viewer.name}
        >
          <div className="dialog-head">
            <strong>{viewer.name}</strong>
            <button
              className="icon-button"
              aria-label="Закрыть просмотр"
              onClick={() => setViewer(null)}
            >
              <X />
            </button>
          </div>
          <div className="shared-viewer-media">
            {viewer.kind === "photo" ? (
              <img src={path(viewer, "content")} alt={viewer.name} />
            ) : viewer.kind === "video" ? (
              <video controls playsInline src={path(viewer, "content")} />
            ) : viewer.kind === "audio" ? (
              <audio controls src={path(viewer, "content")} />
            ) : (
              <FileText size={70} />
            )}
          </div>
          <div className="feature-actions">
            {data.download && (
              <a
                className="primary"
                href={path(viewer, "content") + "?download=true"}
              >
                <ArrowDownToLine size={17} />
                Скачать оригинал
              </a>
            )}
            <button
              className="secondary"
              onClick={() => {
                const i = data.items.findIndex((f: any) => f.id === viewer.id);
                setViewer(
                  data.items[(i + data.items.length - 1) % data.items.length],
                );
              }}
            >
              <ChevronLeft size={17} />
              Предыдущий
            </button>
            <button
              className="secondary"
              onClick={() => {
                const i = data.items.findIndex((f: any) => f.id === viewer.id);
                setViewer(data.items[(i + 1) % data.items.length]);
              }}
            >
              Следующий
              <ChevronRight size={17} />
            </button>
          </div>
        </div>
      )}
    </main>
  );
}
