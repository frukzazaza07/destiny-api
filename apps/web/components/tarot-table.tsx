"use client";

import { useFrame, useLoader, useThree } from "@react-three/fiber";
import { createRoot, type Root } from "react-dom/client";
import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { CanvasTexture, Group, MathUtils, SRGBColorSpace, TextureLoader, Vector3 } from "three";
import type { Locale } from "../lib/i18n";
import type { TarotReadingFlow } from "./use-tarot-reading-flow";

const PAGE_SIZE = 12;
type Point = [number, number, number];

function TableOverlay({ children }: { children: ReactNode }) {
  const { gl } = useThree();
  const root = useRef<Root | null>(null);
  useEffect(() => {
    const element = document.createElement("div");
    gl.domElement.parentElement!.appendChild(element);
    const domRoot = createRoot(element);
    root.current = domRoot;
    return () => {
      root.current = null;
      element.remove();
      queueMicrotask(() => domRoot.unmount());
    };
  }, [gl]);
  useEffect(() => { root.current?.render(children); }, [children, gl]);
  return null;
}
const copy = {
  en: { shuffle: "Shuffle Deck", again: "Shuffle Again", deal: "Deal & reveal", previous: "Previous cards", next: "Next cards", card: "Card", selected: "selected", upright: "Upright", reversed: "Reversed", hint: "Tap the deck to shuffle · Tap cards to select · Tab to focus, Enter to activate" },
  th: { shuffle: "สับไพ่", again: "สับไพ่อีกครั้ง", deal: "แจกและเปิดไพ่", previous: "ไพ่ก่อนหน้า", next: "ไพ่ถัดไป", card: "ไพ่", selected: "เลือกแล้ว", upright: "หัวตั้ง", reversed: "กลับหัว", hint: "แตะสำรับเพื่อสับไพ่ · แตะไพ่เพื่อเลือก · Tab เพื่อเลือกปุ่มและ Enter เพื่อใช้งาน" },
};

// Text is rendered onto the physical surfaces, rather than into a web dialog.
function useLabelTexture(lines: string[], background = "#173c3c") {
  const key = lines.join("\n");
  const texture = useMemo(() => {
    const canvas = document.createElement("canvas");
    canvas.width = 512;
    canvas.height = key.includes("\n") ? 512 : 128;
    const context = canvas.getContext("2d")!;
    context.fillStyle = background;
    context.fillRect(0, 0, 512, canvas.height);
    context.strokeStyle = "#d5ad61";
    context.lineWidth = 6;
    context.strokeRect(8, 8, 496, canvas.height - 16);
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.fillStyle = background === "#eee2c6" ? "#47351e" : "#fff2d6";
    const rows = key.split("\n");
    context.font = `bold ${rows.length > 2 ? 60 : 48}px sans-serif`;
    rows.forEach((line, index) => {
      const metrics = context.measureText(line);
      const scale = Math.min(1, 450 / (metrics.width || 1));
      // Center the visible glyphs, including Thai marks, rather than the font's baseline box.
      const x = canvas.width / 2 + (metrics.actualBoundingBoxLeft - metrics.actualBoundingBoxRight) * scale / 2;
      const y = canvas.height / 2 + (index - (rows.length - 1) / 2) * 86
        + (metrics.actualBoundingBoxAscent - metrics.actualBoundingBoxDescent) / 2;
      context.fillText(line, x, y, 450);
    });
    const result = new CanvasTexture(canvas);
    result.colorSpace = SRGBColorSpace;
    return result;
  }, [key, background]);
  useEffect(() => () => texture.dispose(), [texture]);
  return texture;
}

function useCardBackTexture(number: number) {
  const source = useLoader(TextureLoader, "/images/tarot-card-back.webp");
  const texture = useMemo(() => {
    const canvas = document.createElement("canvas");
    canvas.width = 384;
    canvas.height = 544;
    const context = canvas.getContext("2d")!;
    context.drawImage(source.image, 0, 0, 384, 544);
    context.fillStyle = "#173c3c";
    context.fillRect(148, 414, 88, 88);
    context.font = "bold 50px sans-serif";
    context.textAlign = "center";
    context.fillStyle = "#fff2d6";
    context.fillText(String(number), 192, 476);
    const result = new CanvasTexture(canvas);
    result.colorSpace = SRGBColorSpace;
    return result;
  }, [source, number]);
  useEffect(() => () => texture.dispose(), [texture]);
  return texture;
}

// A keyboard/screen-reader counterpart follows each real mesh. Pointer events
// pass through to the canvas, so mouse and touch always hit the 3D object.
function WorldFocus({ anchor, label, disabled, pressed, onActivate, onFocusChange, children }: {
  anchor: React.RefObject<Group | null>; label: string; disabled: boolean; pressed?: boolean;
  onActivate: () => void; onFocusChange: (focused: boolean) => void; children?: ReactNode;
}) {
  const { gl, camera, size } = useThree();
  const button = useRef<HTMLButtonElement>(null);
  const projected = useMemo(() => new Vector3(), []);
  useFrame(() => {
    if (!anchor.current || !button.current) return;
    anchor.current.getWorldPosition(projected).project(camera);
    button.current.style.left = `${(projected.x + 1) * size.width / 2}px`;
    button.current.style.top = `${(1 - projected.y) * size.height / 2}px`;
    button.current.style.visibility = Math.abs(projected.z) > 1 ? "hidden" : "visible";
  });
  return <TableOverlay><button ref={button} className="table-world-focus" type="button" aria-label={label}
    disabled={disabled} aria-pressed={pressed} onFocus={() => onFocusChange(true)} onBlur={() => onFocusChange(false)}
    onClick={onActivate}>{children ?? label}</button></TableOverlay>;
}

function TableAction({ position, label, enabled, onActivate, width = 1 }: {
  position: Point; label: string; enabled: boolean; onActivate: () => void; width?: number;
}) {
  const anchor = useRef<Group>(null);
  const [focused, setFocused] = useState(false);
  const texture = useLabelTexture([label]);
  return <group ref={anchor} position={position}>
    <mesh rotation={[-Math.PI / 2, 0, 0]} onClick={event => { event.stopPropagation(); if (enabled) onActivate(); }}
      onPointerOver={() => setFocused(true)} onPointerOut={() => setFocused(false)}>
      <planeGeometry args={[width, .32]} />
      <meshBasicMaterial map={texture} color={!enabled ? "#657372" : focused ? "#fff0a5" : "white"} />
    </mesh>
    <WorldFocus anchor={anchor} label={label} disabled={!enabled} onActivate={onActivate} onFocusChange={setFocused} />
  </group>;
}

function TableCard({ index, target, order, enabled, resolved, reversed, dealDelay, reduceMotion, label, onSelect }: {
  index: number; target: Point; order: number; enabled: boolean; resolved: string[] | null;
  reversed: boolean; dealDelay: number; reduceMotion: boolean; label: string; onSelect: () => void;
}) {
  const anchor = useRef<Group>(null);
  const flip = useRef<Group>(null);
  const [focused, setFocused] = useState(false);
  const back = useCardBackTexture(order || index + 1);
  const front = useLabelTexture(resolved ?? ["✦"], "#eee2c6");
  const age = useRef(0);
  const isDealt = dealDelay >= 0;
  useEffect(() => { age.current = 0; }, [isDealt]);
  useFrame((_, delta) => {
    if (!anchor.current || !flip.current) return;
    age.current += delta;
    const speed = reduceMotion ? 1 : 1 - Math.exp(-Math.min(delta, .05) * 9);
    const ready = !isDealt || reduceMotion || age.current > dealDelay;
    if (ready) {
      anchor.current.position.x = MathUtils.lerp(anchor.current.position.x, target[0], speed);
      anchor.current.position.y = MathUtils.lerp(anchor.current.position.y, target[1] + (!isDealt && (order || focused) ? .12 : 0), speed);
      anchor.current.position.z = MathUtils.lerp(anchor.current.position.z, target[2], speed);
    }
    const reveal = resolved && (reduceMotion || age.current > dealDelay + .85);
    flip.current.rotation.x = MathUtils.lerp(flip.current.rotation.x, reveal ? Math.PI / 2 : -Math.PI / 2, speed);
  });
  return <group ref={anchor} position={reduceMotion ? target : [0, 1.22, -.45]}>
    <group ref={flip} rotation={[-Math.PI / 2, 0, 0]} scale={isDealt ? 1.4 : 1}
      onClick={event => { event.stopPropagation(); if (enabled) onSelect(); }}
      onPointerOver={() => setFocused(true)} onPointerOut={() => setFocused(false)}>
      <mesh><boxGeometry args={[.46, .65, .012]} /><meshBasicMaterial map={back} color={focused || order ? "#fff0ac" : "white"} /></mesh>
      {resolved && <mesh position={[0, 0, -.008]} rotation={[0, Math.PI, reversed ? 0 : Math.PI]}><planeGeometry args={[.445, .635]} /><meshBasicMaterial map={front} /></mesh>}
    </group>
    <WorldFocus anchor={anchor} label={label} disabled={!enabled} pressed={order > 0} onActivate={onSelect} onFocusChange={setFocused} />
  </group>;
}

export default function TarotTable({ flow, active, reduceMotion, locale, onAnimation }: {
  flow: TarotReadingFlow; active: boolean; reduceMotion: boolean; locale: Locale; onAnimation: (animation: string | null) => void;
}) {
  const text = copy[locale];
  const [page, setPage] = useState(0);
  const stack = useRef<Group>(null);
  const dealAge = useRef(0);
  const previousAnimation = useRef<string | null>(null);
  const { phase, shuffleVisualStep, selected, cards, reading, shuffle, failureStep } = flow;
  const dealt = phase === "RESOLVING" || cards.length > 0 || failureStep === "RESOLVING";
  const spreading = phase === "SELECTING" || (phase === "SHUFFLING" && shuffleVisualStep === "DEALING");
  const count = shuffle?.cardCount ?? 78;
  const pages = Math.ceil(count / PAGE_SIZE);
  const currentPage = Math.min(page, Math.max(0, pages - 1));
  useEffect(() => setPage(0), [shuffle?.sessionId]);
  useEffect(() => { dealAge.current = 0; }, [dealt, active]);
  useFrame(({ clock }, delta) => {
    dealAge.current += delta;
    const animation = active && dealt && !reduceMotion
      ? dealAge.current < .85 + selected.length * .16 ? "dealing" : cards.length > 0 && dealAge.current < 1.65 + selected.length * .16 ? "reveal" : null
      : null;
    if (animation !== previousAnimation.current) { previousAnimation.current = animation; onAnimation(animation); }
    if (!stack.current) return;
    const mixing = phase === "SHUFFLING" && shuffleVisualStep === "MIXING" && !reduceMotion;
    stack.current.children.forEach((child, index) => {
      child.position.x = mixing ? Math.sin(clock.elapsedTime * 9 + index * .4) * .38 : 0;
      child.position.y = index * .06 + (mixing ? Math.abs(Math.cos(clock.elapsedTime * 7 + index)) * .18 : 0);
      child.rotation.z = mixing ? Math.sin(clock.elapsedTime * 6 + index * .2) * .3 : 0;
    });
  });
  const back = useLoader(TextureLoader, "/images/tarot-card-back.webp");
  const indexes = !active ? [] : dealt ? selected : spreading ? Array.from({ length: Math.min(PAGE_SIZE, count - currentPage * PAGE_SIZE) }, (_, index) => currentPage * PAGE_SIZE + index) : [];
  return <group position={[0, 0, -9.9]}>
    {indexes.map((index, slot) => {
      const order = selected.indexOf(index) + 1;
      const card = dealt ? cards[slot] : undefined;
      const name = reading?.cards.find(item => item.cardId === card?.cardId)?.cardName ?? card?.cardId.replaceAll("_", " ");
      const face = card ? [card.position.replaceAll("_", " "), ...(name ?? "").split(/ (?=[^ ]*$)/), card.orientation === "REVERSED" ? text.reversed : text.upright] : null;
      return <TableCard key={index} index={index} order={order} enabled={active && phase === "SELECTING"}
        target={dealt ? [(slot - (selected.length - 1) / 2) * .95, 1.12, 0] : [(slot % 6 - 2.5) * .55, 1.10, Math.floor(slot / 6) * .78 - .57]}
        resolved={face} reversed={card?.orientation === "REVERSED"} dealDelay={dealt ? slot * .16 : -1} reduceMotion={reduceMotion}
        label={`${text.card} ${index + 1}${order ? `, ${text.selected} ${order}` : ""}`}
        onSelect={() => flow.toggleCard(index)} />;
    })}
    {(!active || (!spreading && !dealt)) && <group ref={stack} position={[0, 1.10, -.25]} onClick={event => { event.stopPropagation(); if (active && !flow.controlsLocked) void flow.startShuffle(); }}>
      {Array.from({ length: 3 }, (_, index) => <mesh key={index} position={[0, index * .06, 0]} rotation={[-Math.PI / 2, 0, 0]}>
        <boxGeometry args={[.6, .85, .055]} /><meshStandardMaterial map={back} />
      </mesh>)}
    </group>}
    {active && <>
      <TableAction position={[0, 1.10, -1.16]} label={shuffle ? text.again : text.shuffle} enabled={!flow.controlsLocked && !dealt} onActivate={() => void flow.startShuffle()} width={1.35} />
      <TableAction position={[0, 1.10, .96]} label={text.deal} enabled={phase === "SELECTING" && selected.length === flow.selectLimit} onActivate={() => void flow.revealAndRead()} width={1.3} />
      {spreading && <>
        <TableAction position={[-1.48, 1.10, .96]} label={text.previous} enabled={phase === "SELECTING" && currentPage > 0} onActivate={() => setPage(currentPage - 1)} />
        <TableAction position={[1.48, 1.10, .96]} label={text.next} enabled={phase === "SELECTING" && currentPage < pages - 1} onActivate={() => setPage(currentPage + 1)} />
      </>}
      <TableOverlay><div className="table-hint"><p>{text.hint}</p><p role="status" aria-live="polite">{shuffle ? `${selected.length}/${flow.selectLimit} ${text.selected}${spreading ? ` · ${currentPage * PAGE_SIZE + 1}–${Math.min((currentPage + 1) * PAGE_SIZE, count)} / ${count}` : ""}` : locale === "th" ? "รอการสับไพ่" : "Awaiting shuffle"}</p></div></TableOverlay>
    </>}
  </group>;
}
