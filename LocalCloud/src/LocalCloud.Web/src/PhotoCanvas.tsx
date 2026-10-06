import { useEffect, useRef, useState } from "react";
type Point = { x: number; y: number };
export function PhotoCanvas({
  src,
  alt,
  zoom,
  onZoom,
  onNavigate,
  rotation = 0,
  exposure = 0,
  crop,
}: {
  src: string;
  alt: string;
  zoom: number;
  onZoom: (z: number) => void;
  onNavigate: (n: number) => void;
  rotation?: number;
  exposure?: number;
  crop?: { x: number; y: number; width: number; height: number };
}) {
  const root = useRef<HTMLDivElement>(null),
    image = useRef<HTMLImageElement>(null),
    points = useRef(new Map<number, Point>()),
    gesture = useRef<{
      point: Point;
      pan: Point;
      distance: number;
      zoom: number;
      swipe: Point;
    } | null>(null);
  const [pan, setPan] = useState<Point>({ x: 0, y: 0 });
  function clamp(p: Point, z: number) {
    const box = root.current?.getBoundingClientRect(),
      im = image.current;
    if (!box || !im) return p;
    const factor = Math.min(
      box.width / (im.naturalWidth || box.width),
      box.height / (im.naturalHeight || box.height),
    );
    const w = (im.naturalWidth || box.width) * factor,
      h = (im.naturalHeight || box.height) * factor;
    return {
      x: Math.max(
        -Math.max(0, (w * z - box.width) / 2),
        Math.min(Math.max(0, (w * z - box.width) / 2), p.x),
      ),
      y: Math.max(
        -Math.max(0, (h * z - box.height) / 2),
        Math.min(Math.max(0, (h * z - box.height) / 2), p.y),
      ),
    };
  }
  useEffect(() => {
    setPan({ x: 0, y: 0 });
    points.current.clear();
    gesture.current = null;
  }, [src]);
  useEffect(() => {
    setPan((p) => clamp(p, zoom));
  }, [zoom]);
  function begin() {
    const ps = [...points.current.values()];
    if (!ps.length) {
      gesture.current = null;
      return;
    }
    const center =
      ps.length > 1
        ? { x: (ps[0].x + ps[1].x) / 2, y: (ps[0].y + ps[1].y) / 2 }
        : ps[0];
    gesture.current = {
      point: center,
      pan,
      distance:
        ps.length > 1 ? Math.hypot(ps[0].x - ps[1].x, ps[0].y - ps[1].y) : 0,
      zoom,
      swipe: ps[0],
    };
  }
  return (
    <div
      ref={root}
      className="photo-canvas"
      aria-label="Фотография: масштабирование и перемещение"
      onDoubleClick={() => {
        onZoom(zoom === 1 ? 2.5 : 1);
        setPan({ x: 0, y: 0 });
      }}
      onWheel={(e) => {
        e.preventDefault();
        onZoom(Math.max(1, Math.min(6, zoom * (e.deltaY < 0 ? 1.12 : 0.89))));
      }}
      onPointerDown={(e) => {
        if (e.button !== 0) return;
        e.currentTarget.setPointerCapture(e.pointerId);
        points.current.set(e.pointerId, { x: e.clientX, y: e.clientY });
        begin();
      }}
      onPointerMove={(e) => {
        if (!points.current.has(e.pointerId) || !gesture.current) return;
        points.current.set(e.pointerId, { x: e.clientX, y: e.clientY });
        const ps = [...points.current.values()],
          g = gesture.current;
        if (ps.length > 1 && g.distance) {
          const center = {
              x: (ps[0].x + ps[1].x) / 2,
              y: (ps[0].y + ps[1].y) / 2,
            },
            z = Math.max(
              1,
              Math.min(
                6,
                (g.zoom * Math.hypot(ps[0].x - ps[1].x, ps[0].y - ps[1].y)) /
                  g.distance,
              ),
            );
          onZoom(z);
          setPan(
            clamp(
              {
                x: g.pan.x + center.x - g.point.x,
                y: g.pan.y + center.y - g.point.y,
              },
              z,
            ),
          );
        } else if (zoom > 1)
          setPan(
            clamp(
              {
                x: g.pan.x + e.clientX - g.point.x,
                y: g.pan.y + e.clientY - g.point.y,
              },
              zoom,
            ),
          );
      }}
      onPointerUp={(e) => {
        const g = gesture.current;
        if (points.current.size === 1 && g && zoom === 1 && !g.distance) {
          const dx = e.clientX - g.swipe.x,
            dy = e.clientY - g.swipe.y;
          if (Math.abs(dx) > 70 && Math.abs(dx) > Math.abs(dy) * 1.5)
            onNavigate(dx < 0 ? 1 : -1);
        }
        points.current.delete(e.pointerId);
        begin();
      }}
      onPointerCancel={() => {
        points.current.clear();
        gesture.current = null;
      }}
    >
      <img
        ref={image}
        src={src}
        alt={alt}
        draggable={false}
        style={{
          transform: `translate3d(${pan.x}px,${pan.y}px,0) scale(${zoom}) rotate(${rotation}deg)`,
          filter: `brightness(${2 ** exposure})`,
          clipPath: crop
            ? `inset(${crop.y * 100}% ${(1 - crop.x - crop.width) * 100}% ${(1 - crop.y - crop.height) * 100}% ${crop.x * 100}%)`
            : undefined,
        }}
      />
    </div>
  );
}
