"use client";

import {
  DndContext,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragMoveEvent,
  type DragStartEvent,
} from "@dnd-kit/core";
import { cn } from "cn";
import { Check } from "lucide-react";
import { createContext, useContext, useEffect, useRef, useState, type MouseEvent } from "react";

import { staffColorClass } from "@/components/staff/staff-bits";
import { appointmentStatusNames, timeOffKindNames, type Appointment, type StaffColor, type TimeOff, type TimeRange } from "@/lib/api/types";
import { DAY_MINUTES, clamp, dayOf, hhmm, layoutBlocks, minutesOf, parseHhmm, snap } from "@/lib/calendar";

import { statusBlockClass } from "./appointment-bits";

/** Bir dakikanın piksel yüksekliği (15 dk = 21 px, 1 saat = 84 px). */
export const PX_PER_MIN = 1.4;
export const STEP = 15;

export type GridColumn = {
  key: string;
  staffId: string;
  date: string;
  title: string;
  subtitle?: string;
  color?: StaffColor;
  /** Çalışma aralıkları ("HH:mm"); dışı gri. */
  ranges: TimeRange[];
  /** Tatil adı veya "Kapalı". */
  closedLabel?: string | null;
};

export type MoveTarget = { staffId: string; date: string; start: number; end: number };

type DragState = { id: string; kind: "move" | "resize"; dy: number } | null;
const DragContext = createContext<DragState>(null);

const movable = (a: Appointment) => !a.saleId && (a.status === "Pending" || a.status === "Confirmed");

/**
 * Saat ızgarası: solda saat ekseni, her kolon bir personel (gün görünümü) veya bir gün (hafta görünümü).
 * Boş alana tıklayınca o saatle yeni randevu; bloğa tıklayınca ayrıntı. Masaüstünde blok sürüklenerek taşınır, alt kenarından
 * çekilerek uzatılır (15 dakikaya yapışır); çakışmayı sunucu engeller, hata gelirse eski yerine döner.
 */
export function TimeGrid({
  columns,
  appointments,
  timeOffs,
  bounds,
  today,
  nowMinute,
  canWrite,
  draggable,
  showCancelled,
  staffColors,
  onCreate,
  onOpen,
  onMove,
}: {
  columns: GridColumn[];
  appointments: Appointment[];
  timeOffs: TimeOff[];
  bounds: { start: number; end: number };
  today: string;
  nowMinute: number;
  canWrite: boolean;
  draggable: boolean;
  showCancelled: boolean;
  staffColors: Record<string, StaffColor>;
  onCreate: (column: GridColumn, minute: number) => void;
  onOpen: (appointment: Appointment) => void;
  onMove: (appointment: Appointment, target: MoveTarget) => void;
}) {
  const [drag, setDrag] = useState<DragState>(null);
  const scroller = useRef<HTMLDivElement>(null);
  const firstMinute = Math.min(
    ...appointments.map((a) => minutesOf(a.startAt)),
    ...columns.flatMap((c) => c.ranges.map((r) => parseHhmm(r.start))),
    columns.some((c) => c.date === today) ? nowMinute : DAY_MINUTES,
  );
  const scrollKey = columns.map((c) => c.key).join("|");

  // Açılışta (ve gün/personel değişince) ızgara ilk randevuya veya çalışma saatinin başına kayar.
  useEffect(() => {
    const el = scroller.current;
    if (!el || firstMinute >= DAY_MINUTES) return;
    el.scrollTop = Math.max(0, (firstMinute - bounds.start - 30) * PX_PER_MIN);
    // Sadece kolonlar değişince; sürükleme sonrası kaydırma kullanıcıyı şaşırtmasın.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scrollKey]);
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }));
  const height = (bounds.end - bounds.start) * PX_PER_MIN;
  const hours: number[] = [];
  for (let m = bounds.start; m <= bounds.end; m += 60) hours.push(m);

  // Sürükleme mesafesi imlecin kendisinden ölçülür (+ ızgaranın kaydırması). dnd-kit'in delta'sı süre uzatmada düğüm ölçüm
  // düzeltmesi yüzünden imlecin gittiği mesafeden büyük çıkıyordu.
  const pointer = useRef<{ startY: number; startScroll: number; y: number } | null>(null);
  useEffect(() => {
    const track = (e: PointerEvent) => {
      if (pointer.current) pointer.current.y = e.clientY;
    };
    window.addEventListener("pointermove", track, { capture: true });
    return () => window.removeEventListener("pointermove", track, { capture: true });
  }, []);
  const draggedMinutes = () => {
    const p = pointer.current;
    if (!p) return 0;
    return snap((p.y - p.startY + (scroller.current?.scrollTop ?? 0) - p.startScroll) / PX_PER_MIN, STEP);
  };

  const onDragStart = (e: DragStartEvent) => {
    const y = (e.activatorEvent as PointerEvent).clientY;
    pointer.current = { startY: y, startScroll: scroller.current?.scrollTop ?? 0, y };
  };

  const parse = (id: string) => {
    const [kind, appointmentId] = id.split(":") as ["move" | "resize", string];
    return { kind, appointment: appointments.find((a) => a.id === appointmentId) };
  };

  const onDragMove = (e: DragMoveEvent) => {
    const { kind, appointment } = parse(String(e.active.id));
    if (appointment) setDrag({ id: appointment.id, kind, dy: draggedMinutes() });
  };

  const onDragEnd = (e: DragEndEvent) => {
    setDrag(null);
    const { kind, appointment } = parse(String(e.active.id));
    if (!appointment) return;
    const dm = draggedMinutes();
    pointer.current = null;
    const start = minutesOf(appointment.startAt);
    const end = start + (new Date(appointment.endAt).getTime() - new Date(appointment.startAt).getTime()) / 60000;
    if (kind === "resize") {
      const nextEnd = clamp(end + dm, start + STEP, DAY_MINUTES - 1);
      if (nextEnd !== end) onMove(appointment, { staffId: appointment.staffMemberId, date: dayOf(appointment.startAt), start, end: nextEnd });
      return;
    }
    const target = (e.over?.data.current as GridColumn | undefined) ?? columns.find((c) => c.staffId === appointment.staffMemberId && c.date === dayOf(appointment.startAt));
    if (!target) return;
    const duration = end - start;
    const nextStart = clamp(start + dm, 0, DAY_MINUTES - duration - 1);
    if (nextStart === start && target.staffId === appointment.staffMemberId && target.date === dayOf(appointment.startAt)) return;
    onMove(appointment, { staffId: target.staffId, date: target.date, start: nextStart, end: nextStart + duration });
  };

  return (
    // Otomatik kaydırma kapalı: kaydırma farkı sürükleme mesafesine eklenip randevuyu beklenenden uzağa taşıyordu.
    <DndContext
      sensors={sensors}
      autoScroll={false}
      onDragStart={onDragStart}
      onDragMove={onDragMove}
      onDragEnd={onDragEnd}
      onDragCancel={() => {
        pointer.current = null;
        setDrag(null);
      }}
    >
      <DragContext.Provider value={drag}>
        <div
          ref={scroller}
          role="region"
          aria-label="Takvim ızgarası"
          tabIndex={0}
          className="max-h-[calc(100dvh-15rem)] overflow-auto border border-border lg:max-h-[calc(100dvh-13rem)]"
        >
          <div className="grid min-w-full" style={{ gridTemplateColumns: `3.5rem repeat(${columns.length}, minmax(${columns.length > 1 ? "9rem" : "0"}, 1fr))` }}>
            {/* Başlık satırı */}
            <div className="sticky top-0 left-0 z-30 border-b border-border bg-white" />
            {columns.map((c) => (
              <div key={c.key} className="sticky top-0 z-20 flex min-h-12 flex-col justify-center border-b border-l border-border bg-white px-2 py-1">
                <span className="flex items-center gap-2 truncate text-sm font-semibold text-black">
                  {c.color && <span aria-hidden className={cn("inline-block size-3 shrink-0 rounded-full", staffColorClass[c.color])} />}
                  <span className="truncate">{c.title}</span>
                </span>
                {(c.subtitle || c.closedLabel) && <span className="truncate text-xs text-neutral-600">{c.closedLabel ?? c.subtitle}</span>}
              </div>
            ))}

            {/* Saat ekseni */}
            <div className="sticky left-0 z-10 border-r border-border bg-white" style={{ height }}>
              {hours.map((m) => (
                // İlk etiket çizginin altında: ortalansa yarısı yapışkan başlığın altında kalırdı.
                <span
                  key={m}
                  className={cn("absolute right-1 text-xs text-neutral-600 tabular-nums", m === bounds.start ? "translate-y-0.5" : "-translate-y-1/2")}
                  style={{ top: (m - bounds.start) * PX_PER_MIN }}
                >
                  {m < DAY_MINUTES ? hhmm(m) : ""}
                </span>
              ))}
            </div>

            {columns.map((c) => (
              <Column
                key={c.key}
                column={c}
                height={height}
                bounds={bounds}
                hours={hours}
                today={today}
                nowMinute={nowMinute}
                canWrite={canWrite}
                draggable={draggable}
                staffColors={staffColors}
                appointments={appointments.filter(
                  (a) => a.staffMemberId === c.staffId && dayOf(a.startAt) === c.date && (showCancelled || a.status !== "Cancelled"),
                )}
                timeOffs={timeOffs.filter((t) => t.staffMemberId === c.staffId)}
                onCreate={onCreate}
                onOpen={onOpen}
              />
            ))}
          </div>
        </div>
      </DragContext.Provider>
    </DndContext>
  );
}

function Column({
  column,
  height,
  bounds,
  hours,
  today,
  nowMinute,
  canWrite,
  draggable,
  staffColors,
  appointments,
  timeOffs,
  onCreate,
  onOpen,
}: {
  column: GridColumn;
  height: number;
  bounds: { start: number; end: number };
  hours: number[];
  today: string;
  nowMinute: number;
  canWrite: boolean;
  draggable: boolean;
  staffColors: Record<string, StaffColor>;
  appointments: Appointment[];
  timeOffs: TimeOff[];
  onCreate: (column: GridColumn, minute: number) => void;
  onOpen: (appointment: Appointment) => void;
}) {
  const { setNodeRef, isOver } = useDroppable({ id: column.key, data: column });
  const top = (m: number) => (m - bounds.start) * PX_PER_MIN;
  const placed = layoutBlocks(
    appointments.map((a) => {
      const start = minutesOf(a.startAt);
      return { id: a.id, start, end: start + (new Date(a.endAt).getTime() - new Date(a.startAt).getTime()) / 60000, appointment: a };
    }),
  );
  const dayStart = new Date(`${column.date}T00:00:00+03:00`).getTime();
  const offs = timeOffs
    .map((t) => ({
      t,
      start: clamp((new Date(t.startAt).getTime() - dayStart) / 60000, 0, DAY_MINUTES),
      end: clamp((new Date(t.endAt).getTime() - dayStart) / 60000, 0, DAY_MINUTES),
    }))
    .filter((o) => o.end > o.start);

  const click = (e: MouseEvent<HTMLDivElement>) => {
    if (!canWrite || e.target !== e.currentTarget) return;
    const y = e.clientY - e.currentTarget.getBoundingClientRect().top;
    onCreate(column, clamp(bounds.start + Math.floor(y / PX_PER_MIN / STEP) * STEP, bounds.start, bounds.end - STEP));
  };

  return (
    <div
      ref={setNodeRef}
      className={cn("relative border-l border-border bg-neutral-100", isOver && "outline-2 -outline-offset-2 outline-brand")}
      style={{ height }}
    >
      {column.ranges.map((r) => (
        <div
          key={r.start}
          aria-hidden
          className="pointer-events-none absolute inset-x-0 bg-white"
          style={{ top: top(parseHhmm(r.start)), height: (parseHhmm(r.end) - parseHhmm(r.start)) * PX_PER_MIN }}
        />
      ))}
      {hours.map((m) => (
        <div key={m} aria-hidden className="pointer-events-none absolute inset-x-0 border-t border-neutral-200" style={{ top: top(m) }} />
      ))}
      {offs.map(({ t, start, end }) => (
        <div
          key={t.id}
          className="pointer-events-none absolute inset-x-0 flex items-start bg-neutral-200 px-1 text-xs text-neutral-700"
          style={{ top: top(Math.max(start, bounds.start)), height: Math.max(0, (Math.min(end, bounds.end) - Math.max(start, bounds.start)) * PX_PER_MIN) }}
        >
          {timeOffKindNames[t.kind]}
          {t.note ? ` · ${t.note}` : ""}
        </div>
      ))}
      {/* Boş alana tıklama katmanı (blokların altında). */}
      <div
        className={cn("absolute inset-0", canWrite && "cursor-copy")}
        onClick={click}
        role="presentation"
        data-testid={`kolon-${column.key}`}
      />
      {column.date === today && nowMinute >= bounds.start && nowMinute <= bounds.end && (
        <div aria-hidden className="pointer-events-none absolute inset-x-0 z-10 border-t-2 border-brand" style={{ top: top(nowMinute) }} />
      )}
      {placed.map((p) => (
        <Block
          key={p.id}
          appointment={p.appointment}
          top={top(p.start)}
          minutes={p.end - p.start}
          lane={p.lane}
          lanes={p.lanes}
          color={staffColors[p.appointment.staffMemberId] ?? "Black"}
          draggable={draggable && canWrite && movable(p.appointment)}
          onOpen={onOpen}
        />
      ))}
    </div>
  );
}

function Block({
  appointment: a,
  top,
  minutes,
  lane,
  lanes,
  color,
  draggable,
  onOpen,
}: {
  appointment: Appointment;
  top: number;
  minutes: number;
  lane: number;
  lanes: number;
  color: StaffColor;
  draggable: boolean;
  onOpen: (appointment: Appointment) => void;
}) {
  const drag = useContext(DragContext);
  // dnd-kit: setNodeRef bir callback ref, listeners olay işleyicileri; render'da ref.current okunmaz.
  const { setNodeRef, listeners, attributes, transform, isDragging } = useDraggable({ id: `move:${a.id}`, disabled: !draggable });
  // Süre uzatma: ölçülen düğüm bloğun üstünde sabit bir çapa, tutamaç sadece tetikleyici. Tutamaç büyüyen bloğun altında
  // kaydığı için ölçülen düğüm olsaydı dnd-kit bu kaymayı sürükleme mesafesine ekliyordu.
  const {
    setNodeRef: setResizeRef,
    setActivatorNodeRef: setResizeHandleRef,
    listeners: resizeListeners,
  } = useDraggable({ id: `resize:${a.id}`, disabled: !draggable });
  const resizing = drag?.id === a.id && drag.kind === "resize" ? drag.dy : 0;
  const moving = drag?.id === a.id && drag.kind === "move" ? drag.dy : 0;
  const shownMinutes = Math.max(STEP, minutes + resizing);
  const start = minutesOf(a.startAt) + moving;
  const label = `${hhmm(start)}-${hhmm(start + shownMinutes)} ${a.contactName}, ${a.serviceName}, ${appointmentStatusNames[a.status]}`;

  return (
    <div
      ref={setNodeRef}
      className={cn("absolute z-[5] px-0.5", isDragging && "z-20 opacity-90")}
      style={{
        top,
        height: Math.max(shownMinutes * PX_PER_MIN, 22),
        left: `${(lane / lanes) * 100}%`,
        width: `${100 / lanes}%`,
        transform: transform ? `translate3d(${transform.x}px, ${transform.y}px, 0)` : undefined,
      }}
    >
      <button
        type="button"
        aria-label={label}
        title={label}
        onClick={() => onOpen(a)}
        {...(draggable ? { ...listeners, ...attributes } : {})}
        aria-roledescription={draggable ? "Sürüklenebilir randevu" : undefined}
        className={cn(
          "relative flex h-full w-full flex-col overflow-hidden rounded-md py-0.5 pr-1 pl-2.5 text-left text-xs leading-tight",
          statusBlockClass[a.status],
          draggable ? "cursor-grab touch-none" : "cursor-pointer",
        )}
      >
        <span aria-hidden className={cn("absolute inset-y-0 left-0 w-1.5", staffColorClass[color])} />
        <span className="flex items-center gap-1 font-semibold tabular-nums">
          {a.status === "Completed" && <Check aria-hidden className="size-3" />}
          {hhmm(start)}
        </span>
        <span className="truncate font-semibold">{a.contactName}</span>
        {shownMinutes >= 45 && <span className="truncate text-neutral-700">{a.serviceName}</span>}
      </button>
      {draggable && <span ref={setResizeRef} aria-hidden className="pointer-events-none absolute top-0 left-0 size-0" />}
      {draggable && (
        <div
          ref={setResizeHandleRef}
          {...resizeListeners}
          aria-hidden
          className="absolute inset-x-1 bottom-0 h-2 cursor-ns-resize touch-none"
        />
      )}
    </div>
  );
}
