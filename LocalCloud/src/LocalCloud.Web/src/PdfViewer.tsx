import { useEffect, useRef, useState } from "react";
import * as pdfjs from "pdfjs-dist";
import worker from "pdfjs-dist/build/pdf.worker.min.mjs?url";
import { PDFDocument, degrees, rgb } from "pdf-lib";
import fontkit from "@pdf-lib/fontkit";
import {
  ChevronLeft,
  ChevronRight,
  RotateCw,
  Trash2,
  Plus,
  Save,
  Loader2,
} from "lucide-react";
import { addUploads } from "./uploads";
import { type FileEntry } from "./api";
pdfjs.GlobalWorkerOptions.workerSrc = worker;
type Note = { page: number; text: string; x: number; y: number };
export default function PdfViewer({ file }: { file: FileEntry }) {
  const [doc, setDoc] = useState<pdfjs.PDFDocumentProxy | null>(null),
    [page, setPage] = useState(1),
    [scale, setScale] = useState(1),
    [rotation, setRotation] = useState<Record<number, number>>({}),
    [removed, setRemoved] = useState<number[]>([]),
    [notes, setNotes] = useState<Note[]>([]),
    [text, setText] = useState(""),
    [placing, setPlacing] = useState(false),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    [saving, setSaving] = useState(false),
    [done, setDone] = useState("");
  const canvas = useRef<HTMLCanvasElement>(null),
    stage = useRef<HTMLDivElement>(null),
    render = useRef<pdfjs.RenderTask | null>(null),
    viewport = useRef<pdfjs.PageViewport | null>(null),
    raw = useRef<Uint8Array | null>(null);
  const editAllowed = file.size <= 64 * 1024 * 1024;
  useEffect(() => {
    let live = true;
    const task = pdfjs.getDocument({
      url: "/api/files/" + file.id + "/pdf",
      enableXfa: false,
      cMapUrl: "/pdf-assets/cmaps/",
      cMapPacked: true,
      standardFontDataUrl: "/pdf-assets/standard_fonts/",
      wasmUrl: "/pdf-assets/wasm/",
    });
    setBusy(true);
    void task.promise
      .then((d) => {
        if (live) {
          setDoc(d);
          setPage(1);
          setBusy(false);
        }
      })
      .catch(() => {
        if (live) {
          setError(
            "Не удалось открыть PDF. Возможно, он защищён паролем или повреждён. Оригинал можно скачать.",
          );
          setBusy(false);
        }
      });
    return () => {
      live = false;
      render.current?.cancel();
      void task.destroy();
    };
  }, [file.id]);
  useEffect(() => {
    let live = true;
    async function show() {
      if (!doc || !canvas.current) return;
      render.current?.cancel();
      try {
        const p = await doc.getPage(page);
        if (!live || !canvas.current) return;
        const initial = p.getViewport({ scale: 1 }),
          width = Math.max(
            260,
            Math.min(stage.current?.clientWidth || 700, 1000) - 32,
          ),
          v = p.getViewport({
            scale: (width / initial.width) * scale,
            rotation: (p.rotate + (rotation[page] || 0)) % 360,
          }),
          density = Math.min(devicePixelRatio || 1, 2);
        viewport.current = v;
        const c = canvas.current;
        c.width = Math.floor(v.width * density);
        c.height = Math.floor(v.height * density);
        c.style.width = v.width + "px";
        c.style.height = v.height + "px";
        render.current = p.render({
          canvas: c,
          viewport: v,
          transform: density !== 1 ? [density, 0, 0, density, 0, 0] : undefined,
        });
        await render.current.promise;
      } catch (e) {
        if (live && (e as Error).name !== "RenderingCancelledException")
          setError("Не удалось отобразить страницу.");
      }
    }
    void show();
    return () => {
      live = false;
      render.current?.cancel();
    };
  }, [doc, page, scale, rotation]);
  async function save() {
    if (!doc) return;
    setSaving(true);
    setError("");
    try {
      raw.current ??= new Uint8Array(
        await (await fetch("/api/files/" + file.id + "/pdf")).arrayBuffer(),
      );
      const pdf = await PDFDocument.load(raw.current);
      pdf.registerFontkit(fontkit);
      const font = notes.length
        ? await pdf.embedFont(
            await (await fetch("/fonts/DejaVuSans.ttf")).arrayBuffer(),
            { subset: true },
          )
        : null;
      for (const [i, angle] of Object.entries(rotation)) {
        const p = pdf.getPage(Number(i) - 1);
        p.setRotation(degrees((p.getRotation().angle + angle) % 360));
      }
      for (const n of notes) {
        const p = pdf.getPage(n.page - 1);
        p.drawText(n.text, {
          x: n.x,
          y: n.y,
          size: 14,
          font: font!,
          color: rgb(0.12, 0.18, 0.27),
          maxWidth: Math.max(20, p.getWidth() - n.x - 15),
        });
      }
      for (const i of [...removed].sort((a, b) => b - a)) pdf.removePage(i - 1);
      const data = await pdf.save();
      const copy = new Uint8Array(data).buffer;
      const edited = new File(
        [copy],
        file.name.replace(/\.pdf$/i, "") + "-edited.pdf",
        { type: "application/pdf" },
      );
      addUploads([edited], file.path.split("/").slice(0, -1).join("/"));
      setDone("Копия добавлена в очередь загрузок. Оригинал сохранён.");
    } catch {
      setError(
        "Не удалось сохранить PDF. Защищённые документы нельзя редактировать.",
      );
    } finally {
      setSaving(false);
    }
  }
  return (
    <div
      className="pdf-viewer"
      onTouchStart={(e) => e.stopPropagation()}
      onTouchEnd={(e) => e.stopPropagation()}
    >
      <div className="pdf-toolbar">
        <button
          aria-label="Предыдущая страница"
          disabled={page <= 1}
          onClick={() => setPage(page - 1)}
        >
          <ChevronLeft size={20} />
        </button>
        <span>
          {page} / {doc?.numPages || "…"}
        </span>
        <button
          aria-label="Следующая страница"
          disabled={!doc || page >= doc.numPages}
          onClick={() => setPage(page + 1)}
        >
          <ChevronRight size={20} />
        </button>
        <select
          aria-label="Масштаб PDF"
          value={scale}
          onChange={(e) => setScale(Number(e.target.value))}
        >
          {[0.75, 1, 1.25, 1.5, 2].map((n) => (
            <option key={n} value={n}>
              {Math.round(n * 100)}%
            </option>
          ))}
        </select>
        <button
          aria-label="Повернуть страницу PDF"
          disabled={!doc || !editAllowed}
          onClick={() =>
            setRotation({
              ...rotation,
              [page]: ((rotation[page] || 0) + 90) % 360,
            })
          }
        >
          <RotateCw size={19} />
        </button>
        <button
          aria-label="Удалить страницу из копии PDF"
          disabled={!doc || !editAllowed || doc.numPages - removed.length <= 1}
          className={removed.includes(page) ? "chosen" : ""}
          onClick={() =>
            setRemoved(
              removed.includes(page)
                ? removed.filter((n) => n !== page)
                : [...removed, page],
            )
          }
        >
          <Trash2 size={18} />
        </button>
      </div>
      <div className="pdf-stage" ref={stage}>
        <div
          className="pdf-page"
          style={{ opacity: removed.includes(page) ? 0.35 : 1 }}
        >
          <canvas
            ref={canvas}
            aria-label={"Страница PDF " + page}
            onClick={(e) => {
              if (!placing || !text.trim() || !viewport.current) return;
              const rect = e.currentTarget.getBoundingClientRect(),
                point = viewport.current.convertToPdfPoint(
                  e.clientX - rect.left,
                  e.clientY - rect.top,
                );
              setNotes([...notes, { page, text, x: point[0], y: point[1] }]);
              setPlacing(false);
              setText("");
            }}
          />
          {notes
            .filter((n) => n.page === page)
            .map((n, i) => {
              const p = viewport.current?.convertToViewportPoint(n.x, n.y) || [
                0, 0,
              ];
              return (
                <span
                  className="pdf-note"
                  key={i}
                  style={{ left: p[0], top: p[1] - 18 }}
                >
                  {n.text}
                </span>
              );
            })}
        </div>
        {busy && <Loader2 className="spinner" />}
      </div>
      {editAllowed && doc && (
        <div className="pdf-editor">
          <label className="field">
            Текстовая пометка
            <input
              maxLength={300}
              value={text}
              onChange={(e) => setText(e.target.value)}
              placeholder="Введите текст, затем выберите место на странице"
            />
          </label>
          <div className="feature-actions">
            <button
              className="secondary"
              disabled={!text.trim()}
              onClick={() => setPlacing(!placing)}
            >
              <Plus size={16} />
              {placing ? "Нажмите на страницу" : "Разместить текст"}
            </button>
            <button
              className="primary"
              disabled={
                saving ||
                (!notes.length &&
                  !removed.length &&
                  !Object.values(rotation).some(Boolean))
              }
              onClick={() => void save()}
            >
              {saving ? (
                <Loader2 className="spinner" size={17} />
              ) : (
                <Save size={17} />
              )}
              Сохранить копию
            </button>
            <button
              className="text-button"
              onClick={() => {
                setNotes([]);
                setRemoved([]);
                setRotation({});
                setDone("");
              }}
            >
              Сбросить
            </button>
          </div>
          <small>
            Поворот, удаление страниц и новые пометки. Существующий текст PDF не
            изменяется.
          </small>
        </div>
      )}
      {!editAllowed && (
        <p className="quiet-note">
          Для этого большого PDF доступен просмотр. Редактирование копии — до 64
          МБ.
        </p>
      )}
      {error && <p className="danger">{error}</p>}
      {done && <p role="status">{done}</p>}
    </div>
  );
}
