"use client";

import dynamic from "next/dynamic";
import { useCallback, useEffect, useRef, useState } from "react";
import { astrologyCopy, type Locale } from "../lib/i18n";
import AstrologyConsultation from "./astrology-consultation";
import { useAstrologyReadingFlow } from "./use-astrology-reading-flow";
import ReadingClient from "./reading-client";
import type { Movement, QualityProfile, ShopInteraction, ShopZone } from "./destiny-shop-canvas";
import { useTarotReadingFlow } from "./use-tarot-reading-flow";

const DestinyShopCanvas = dynamic(() => import("./destiny-shop-canvas"), {
  ssr: false,
  loading: () => (
    <div className="destiny-shop-loading" role="status">
      <span aria-hidden="true" />
      Loading the destiny shop…
    </div>
  ),
});

type WebGlStatus = "checking" | "available" | "unavailable";

const shopCopy = {
  en: {
    eyebrow: "A walk-in Tarot experience",
    title: "Step inside the destiny shop",
    intro: "Walk through a Thai-inspired reception and service gallery to meet the Tarot advisor in a private consultation room.",
    enter: "Enter 3D shop",
    direct: "Start Tarot now",
    exit: "Exit 3D view",
    loading: "Checking 3D support…",
    loadingScene: "Preparing the reception, gallery, and Tarot room…",
    unsupported: "This device cannot open the 3D shop, but the complete Tarot reading remains available below.",
    contextLost: "The 3D view stopped unexpectedly. Your reading is safe in the accessible view below.",
    walkHint: "WASD or arrows to walk · drag to look · E to interact",
    movement: "Movement controls",
    forward: "Walk forward",
    backward: "Walk backward",
    left: "Walk left",
    right: "Walk right",
    consult: "Sit for a Tarot reading",
    interact: "Interact",
    roomLabel: "Interactive three-dimensional Tarot consultation room",
    fallbackLabel: "Three-zone destiny shop preview",
    closeReading: "Leave consultation",
    restart: "Start a new reading",
    quality: "Visual quality",
    qualityLow: "Low",
    qualityStandard: "Standard",
    qualityHigh: "High",
    audioMuted: "Audio muted",
    audioOn: "Ambient audio on",
    zones: { ENTRANCE: "Entrance & reception", GALLERY: "Service gallery", TAROT_ROOM: "Private Tarot room" },
    approach: {
      ENTRANCE: "Welcome. Walk through reception toward the service gallery.",
      GALLERY: "Tarot is open. Other services are coming later.",
      TAROT_ROOM: "Approach the advisor's table when you are ready.",
    },
    reception: "Reception welcomes you. Continue through the carved doorway.",
    future: "This service is coming later. Tarot is open in the room ahead.",
    tarotReady: "The Tarot advisor is ready to welcome you.",
    comingSoon: "Coming later",
    tarotOpen: "Tarot · Open",
    consultationEyebrow: "Private Tarot consultation",
    consultationIntro: "Choose your focus, then tap the deck on the table to shuffle. Select your cards and tap Deal & reveal.",
  },
  th: {
    eyebrow: "ประสบการณ์ดูไพ่ทาโรต์เสมือนจริง",
    title: "ก้าวเข้าสู่ร้านแห่งโชคชะตา",
    intro: "เดินผ่านโถงต้อนรับและแกลเลอรีบริการที่ได้แรงบันดาลใจจากไทย เพื่อพบที่ปรึกษาไพ่ทาโรต์ในห้องส่วนตัว",
    enter: "เข้าสู่ร้าน 3 มิติ",
    direct: "เริ่มดูไพ่ทันที",
    exit: "ออกจากมุมมอง 3 มิติ",
    loading: "กำลังตรวจสอบการรองรับ 3 มิติ…",
    loadingScene: "กำลังเตรียมโถงต้อนรับ แกลเลอรี และห้องไพ่ทาโรต์…",
    unsupported: "อุปกรณ์นี้ไม่สามารถเปิดร้าน 3 มิติได้ แต่ยังดูไพ่แบบเต็มด้านล่างได้",
    contextLost: "มุมมอง 3 มิติหยุดทำงาน คำอ่านของคุณยังปลอดภัยในมุมมองด้านล่าง",
    walkHint: "เดินด้วย WASD หรือลูกศร · ลากเพื่อมอง · กด E เพื่อโต้ตอบ",
    movement: "ปุ่มควบคุมการเดิน",
    forward: "เดินไปข้างหน้า",
    backward: "เดินถอยหลัง",
    left: "เดินไปทางซ้าย",
    right: "เดินไปทางขวา",
    consult: "นั่งลงเพื่อดูไพ่ทาโรต์",
    interact: "โต้ตอบ",
    roomLabel: "ร้านให้คำปรึกษาไพ่ทาโรต์สามมิติแบบโต้ตอบ",
    fallbackLabel: "ภาพตัวอย่างร้านแห่งโชคชะตาสามโซน",
    closeReading: "ออกจากห้องปรึกษา",
    restart: "เริ่มอ่านไพ่ใหม่",
    quality: "คุณภาพภาพ",
    qualityLow: "ต่ำ",
    qualityStandard: "มาตรฐาน",
    qualityHigh: "สูง",
    audioMuted: "ปิดเสียง",
    audioOn: "เปิดเสียงบรรยากาศ",
    zones: { ENTRANCE: "ทางเข้าและต้อนรับ", GALLERY: "แกลเลอรีบริการ", TAROT_ROOM: "ห้องไพ่ทาโรต์ส่วนตัว" },
    approach: {
      ENTRANCE: "ยินดีต้อนรับ เดินผ่านโถงต้อนรับไปยังแกลเลอรีบริการ",
      GALLERY: "ไพ่ทาโรต์เปิดให้บริการ ส่วนบริการอื่นกำลังจะมา",
      TAROT_ROOM: "เข้าใกล้โต๊ะที่ปรึกษาเมื่อคุณพร้อม",
    },
    reception: "แผนกต้อนรับยินดีต้อนรับ เดินต่อผ่านซุ้มประตูแกะสลัก",
    future: "บริการนี้กำลังจะมา ไพ่ทาโรต์เปิดอยู่ในห้องด้านหน้า",
    tarotReady: "ที่ปรึกษาไพ่ทาโรต์พร้อมต้อนรับคุณแล้ว",
    comingSoon: "เร็ว ๆ นี้",
    tarotOpen: "ไพ่ทาโรต์ · เปิด",
    consultationEyebrow: "ห้องปรึกษาไพ่ทาโรต์ส่วนตัว",
    consultationIntro: "เลือกหัวข้อ แล้วแตะสำรับบนโต๊ะเพื่อสับไพ่ เลือกไพ่แล้วแตะแจกและเปิดไพ่",
  },
} as const;

const flowMessages = {
  en: { questionRequired: "Enter a question before shuffling the deck.", shuffleError: "Could not shuffle the deck.", revealError: "Could not reveal the selected cards.", readingError: "Could not generate the reading.", unavailableError: "The reading service is unavailable. Please try again." },
  th: { questionRequired: "กรุณาใส่คำถามก่อนสับไพ่", shuffleError: "ไม่สามารถสับไพ่ได้", revealError: "ไม่สามารถเปิดไพ่ที่เลือกได้", readingError: "ไม่สามารถสร้างคำทำนายได้", unavailableError: "ไม่สามารถเชื่อมต่อบริการอ่านไพ่ได้ กรุณาลองอีกครั้ง" },
} as const;

export default function ImmersiveReadingJourney({ initialLocale }: { initialLocale: Locale }) {
  const text = shopCopy[initialLocale];
  const astrologyText = astrologyCopy[initialLocale];
  const astrology = useAstrologyReadingFlow(initialLocale);
  const [astrologyOpen, setAstrologyOpen] = useState(false);
  const astrologyRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    const openFromLink = () => { if (window.location.hash === "#astrology-consultation") setAstrologyOpen(true); };
    openFromLink(); window.addEventListener("hashchange", openFromLink);
    return () => window.removeEventListener("hashchange", openFromLink);
  }, []);
  const shopRef = useRef<HTMLElement | null>(null);
  const enterButtonRef = useRef<HTMLButtonElement | null>(null);
  const consultationRef = useRef<HTMLDivElement | null>(null);
  const [webGlStatus, setWebGlStatus] = useState<WebGlStatus>("checking");
  const [entered, setEntered] = useState(false);
  const [sceneReady, setSceneReady] = useState(false);
  const [consultationOpen, setConsultationOpen] = useState(false);
  const [interaction, setInteraction] = useState<ShopInteraction>(null);
  const [zone, setZone] = useState<ShopZone>("ENTRANCE");
  const [movement, setMovement] = useState<Movement>({ x: 0, z: 0 });
  const [joystick, setJoystick] = useState<Movement>({ x: 0, z: 0 });
  const [reduceMotion, setReduceMotion] = useState(false);
  const [quality, setQuality] = useState<QualityProfile>("STANDARD");
  const [audioEnabled, toggleAudio] = useAmbientAudio();
  const [fallbackMessage, setFallbackMessage] = useState<string | null>(null);
  const flow = useTarotReadingFlow({ locale: initialLocale, reduceMotion, messages: flowMessages[initialLocale] });

  useEffect(() => {
    const media = window.matchMedia("(prefers-reduced-motion: reduce)");
    const updateMotion = () => setReduceMotion(media.matches);
    updateMotion();
    media.addEventListener("change", updateMotion);
    let available = false;
    try {
      const canvas = document.createElement("canvas");
      available = Boolean(canvas.getContext("webgl2", { failIfMajorPerformanceCaveat: true }) ?? canvas.getContext("webgl", { failIfMajorPerformanceCaveat: true }));
    } catch {
      available = false;
    }
    setWebGlStatus(available ? "available" : "unavailable");
    if (navigator.hardwareConcurrency && navigator.hardwareConcurrency <= 4) setQuality("LOW");
    return () => media.removeEventListener("change", updateMotion);
  }, []);

  const openConsultation = useCallback(() => {
    setMovement({ x: 0, z: 0 });
    setJoystick({ x: 0, z: 0 });
    if (entered) {
      setConsultationOpen(true);
      window.requestAnimationFrame(() => consultationRef.current?.focus({ preventScroll: true }));
      return;
    }
    window.requestAnimationFrame(() => {
      consultationRef.current?.focus({ preventScroll: true });
      consultationRef.current?.scrollIntoView({ behavior: reduceMotion ? "auto" : "smooth", block: "start" });
    });
  }, [entered, reduceMotion]);

  const closeConsultation = useCallback(() => {
    setConsultationOpen(false);
    window.requestAnimationFrame(() => shopRef.current?.focus({ preventScroll: true }));
  }, []);

  const exitShop = useCallback(() => {
    setMovement({ x: 0, z: 0 });
    setJoystick({ x: 0, z: 0 });
    setConsultationOpen(false);
    setEntered(false);
    window.requestAnimationFrame(() => enterButtonRef.current?.focus({ preventScroll: true }));
  }, []);

  const handleInteract = useCallback(() => {
    if (interaction === "TAROT") openConsultation();
    if (interaction === "THAI_ASTROLOGY") {
      setAstrologyOpen(true); setMovement({ x: 0, z: 0 }); setJoystick({ x: 0, z: 0 });
      window.requestAnimationFrame(() => astrologyRef.current?.focus());
    }
  }, [interaction, openConsultation]);

  const meetAstrology = () => {
    setConsultationOpen(false); setAstrologyOpen(true);
    setMovement({ x: 0, z: 0 }); setJoystick({ x: 0, z: 0 });
    window.requestAnimationFrame(() => { astrologyRef.current?.focus(); astrologyRef.current?.scrollIntoView({ block: "start" }); });
  };

  const handleContextLost = useCallback(() => {
    setFallbackMessage(text.contextLost);
    setWebGlStatus("unavailable");
    setEntered(false);
    setConsultationOpen(false);
  }, [text.contextLost]);

  useEffect(() => {
    if (!entered) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    const outsideShop = Array.from(document.querySelectorAll<HTMLElement>(".site-header, .publisher-content, .site-footer"));
    const previousAria = outsideShop.map((element) => element.getAttribute("aria-hidden"));
    outsideShop.forEach((element) => {
      element.inert = true;
      element.setAttribute("aria-hidden", "true");
    });
    const onEscape = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      if (astrologyOpen) setAstrologyOpen(false);
      else if (consultationOpen) closeConsultation();
      else exitShop();
    };
    window.addEventListener("keydown", onEscape);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener("keydown", onEscape);
      outsideShop.forEach((element, index) => {
        element.inert = false;
        const aria = previousAria[index];
        if (aria === null) element.removeAttribute("aria-hidden");
        else element.setAttribute("aria-hidden", aria);
      });
    };
  }, [closeConsultation, consultationOpen, astrologyOpen, entered, exitShop]);

  const status = interaction === "TAROT" ? text.tarotReady : interaction === "RECEPTION" ? text.reception : interaction === "COMING_SOON" ? text.future : text.approach[zone];
  const combinedMovement = { x: movement.x + joystick.x, z: movement.z + joystick.z };

  return (
    <>
      <section ref={shopRef} className={`destiny-shop ${entered ? "is-entered" : ""} ${consultationOpen ? "is-consulting" : ""}`} aria-label={text.roomLabel} tabIndex={-1}>
        {!entered && <div className="destiny-shop-copy">
          <p className="eyebrow">{text.eyebrow}</p>
          <p className="destiny-shop-title">{text.title}</p>
          <p>{text.intro}</p>
          <div className="destiny-shop-actions">
            {webGlStatus !== "unavailable" && <button ref={enterButtonRef} type="button" className="shop-primary" disabled={webGlStatus === "checking"} onClick={() => { setSceneReady(false); setEntered(true); }}>
              {webGlStatus === "checking" ? text.loading : text.enter}
            </button>}
            <button type="button" className="shop-text-action" onClick={openConsultation}>{text.direct}</button>
            <button type="button" className="shop-text-action" onClick={meetAstrology}>{astrologyText.meet}</button>
          </div>
        </div>}

        <div className="destiny-shop-stage">
          {entered && webGlStatus === "available" ? (
            <DestinyShopCanvas
              locale={initialLocale}
              movement={combinedMovement}
              reduceMotion={reduceMotion}
              quality={quality}
              consultationOpen={consultationOpen}
              astrologyOpen={astrologyOpen}
              astrologyState={astrology.state}
              onAstrology={meetAstrology}
              flow={flow}
              onInteractionChange={setInteraction}
              onZoneChange={setZone}
              onInteract={handleInteract}
              onReady={() => setSceneReady(true)}
              onContextLost={handleContextLost}
            />
          ) : <ShopPreview label={text.fallbackLabel} unavailable={webGlStatus === "unavailable"} unavailableMessage={fallbackMessage ?? text.unsupported} />}

          {entered && !sceneReady && <div className="destiny-shop-loading scene-loading" role="status"><span aria-hidden="true" />{text.loadingScene}</div>}

          {entered && webGlStatus === "available" && <>
            <header className="shop-topbar">
              <div className="shop-zone-indicator" aria-live="polite"><span>{text.zones[zone]}</span><i>{zone === "ENTRANCE" ? "1 / 3" : zone === "GALLERY" ? "2 / 3" : "3 / 3"}</i></div>
              <label className="shop-quality">{text.quality}<select value={quality} onChange={(event) => setQuality(event.target.value as QualityProfile)}><option value="LOW">{text.qualityLow}</option><option value="STANDARD">{text.qualityStandard}</option><option value="HIGH">{text.qualityHigh}</option></select></label>
              <button type="button" className="shop-icon-action" aria-pressed={audioEnabled} onClick={toggleAudio}>{audioEnabled ? "🔊" : "🔇"}<span>{audioEnabled ? text.audioOn : text.audioMuted}</span></button>
              <button type="button" className="shop-secondary" onClick={exitShop}>{text.exit}</button>
              <button type="button" className="shop-secondary" onClick={meetAstrology}>{astrologyText.meet}</button>
            </header>
            {!consultationOpen && !astrologyOpen && <div className="service-legend" aria-label={initialLocale === "th" ? "สถานะบริการ" : "Service status"}><span className="is-open">{text.tarotOpen}</span><span className="is-open">{astrologyText.open}</span><span>{text.comingSoon}</span></div>}
            {!consultationOpen && !astrologyOpen && <div className="destiny-shop-hud">
              <p className={interaction === "TAROT" ? "is-ready" : ""} role="status" aria-live="polite">{interaction === "THAI_ASTROLOGY" ? astrologyText.meet : status}</p>
              <small>{text.walkHint}</small>
              <TouchJoystick value={joystick} onChange={setJoystick} />
              <div className="shop-keypad" role="group" aria-label={text.movement}>
                <HoldButton label={text.forward} symbol="W" onStart={() => setMovement({ x: 0, z: -1 })} onStop={() => setMovement({ x: 0, z: 0 })} />
                <HoldButton label={text.left} symbol="A" onStart={() => setMovement({ x: -1, z: 0 })} onStop={() => setMovement({ x: 0, z: 0 })} />
                <HoldButton label={text.backward} symbol="S" onStart={() => setMovement({ x: 0, z: 1 })} onStop={() => setMovement({ x: 0, z: 0 })} />
                <HoldButton label={text.right} symbol="D" onStart={() => setMovement({ x: 1, z: 0 })} onStop={() => setMovement({ x: 0, z: 0 })} />
              </div>
              <button type="button" className={`shop-consult-action ${interaction === "TAROT" ? "is-ready" : ""}`} onClick={handleInteract} disabled={interaction !== "TAROT" && interaction !== "THAI_ASTROLOGY"}>{interaction === "TAROT" ? text.consult : text.interact}</button>
            </div>}
          </>}
        </div>

        {entered && consultationOpen && <div className="inworld-consultation" role="region" aria-label={text.consultationEyebrow}>
          <div className="inworld-consultation-toolbar">
            <div><p className="eyebrow">{text.consultationEyebrow}</p><p>{text.consultationIntro}</p></div>
            <div><button type="button" className="shop-text-action" onClick={flow.resetReadingFlow}>{text.restart}</button><button type="button" className="shop-secondary" onClick={closeConsultation}>{text.closeReading}</button></div>
          </div>
          <div id="tarot-consultation" ref={consultationRef} className="inworld-reading-scroll" tabIndex={-1}><ReadingClient initialLocale={initialLocale} flow={flow} immersive /></div>
        </div>}
      </section>
        <div id="astrology-consultation" ref={astrologyRef} tabIndex={-1} hidden={!astrologyOpen} className={entered ? "astrology-panel" : "astrology-accessible"}>
          <button className="shop-secondary" type="button" onClick={() => { setAstrologyOpen(false); shopRef.current?.focus(); }}>{text.closeReading}</button>
          {astrologyOpen && <AstrologyConsultation locale={initialLocale} flow={astrology} />}
        </div>

      {!entered && <div id="tarot-consultation" ref={consultationRef} className="tarot-consultation-anchor" tabIndex={-1}>
        <div className="consultation-bridge"><div><p className="eyebrow">{text.consultationEyebrow}</p><p>{text.consultationIntro}</p></div></div>
        <ReadingClient initialLocale={initialLocale} flow={flow} />
      </div>}
    </>
  );
}

function ShopPreview({ label, unavailable, unavailableMessage }: { label: string; unavailable: boolean; unavailableMessage: string }) {
  return <div className="destiny-shop-preview" role="img" aria-label={label}><div className="preview-window" aria-hidden="true" /><div className="preview-shelf preview-shelf-left" aria-hidden="true" /><div className="preview-shelf preview-shelf-right" aria-hidden="true" /><div className="preview-advisor" aria-hidden="true"><span /></div><div className="preview-table" aria-hidden="true"><i /><i /><i /></div>{unavailable && <p>{unavailableMessage}</p>}</div>;
}

function HoldButton({ label, symbol, onStart, onStop }: { label: string; symbol: string; onStart: () => void; onStop: () => void }) {
  return <button type="button" aria-label={label} onPointerDown={(event) => { event.currentTarget.setPointerCapture(event.pointerId); onStart(); }} onPointerUp={onStop} onPointerCancel={onStop} onLostPointerCapture={onStop} onKeyDown={(event) => { if ((event.key === "Enter" || event.key === " ") && !event.repeat) onStart(); }} onKeyUp={(event) => { if (event.key === "Enter" || event.key === " ") onStop(); }}><span aria-hidden="true">{symbol}</span></button>;
}

function TouchJoystick({ value, onChange }: { value: Movement; onChange: (movement: Movement) => void }) {
  const surface = useRef<HTMLDivElement | null>(null);
  const update = (clientX: number, clientY: number) => {
    const bounds = surface.current?.getBoundingClientRect();
    if (!bounds) return;
    const x = MathUtilsClamp((clientX - (bounds.left + bounds.width / 2)) / (bounds.width * 0.34));
    const z = MathUtilsClamp((clientY - (bounds.top + bounds.height / 2)) / (bounds.height * 0.34));
    onChange({ x, z });
  };
  return <div ref={surface} className="shop-joystick" aria-hidden="true" onPointerDown={(event) => { event.currentTarget.setPointerCapture(event.pointerId); update(event.clientX, event.clientY); }} onPointerMove={(event) => { if (event.currentTarget.hasPointerCapture(event.pointerId)) update(event.clientX, event.clientY); }} onPointerUp={() => onChange({ x: 0, z: 0 })} onPointerCancel={() => onChange({ x: 0, z: 0 })}>
    <span className="joystick-knob" aria-hidden="true" style={{ transform: `translate(${value.x * 24}px, ${value.z * 24}px)` }} />
  </div>;
}

function MathUtilsClamp(value: number) { return Math.max(-1, Math.min(1, value)); }

function useAmbientAudio() {
  const [enabled, setEnabled] = useState(false);
  const audio = useRef<{ context: AudioContext; oscillators: OscillatorNode[] } | null>(null);

  const stop = useCallback(() => {
    const current = audio.current;
    audio.current = null;
    if (current) {
      current.oscillators.forEach((oscillator) => oscillator.stop());
      void current.context.close();
    }
    setEnabled(false);
  }, []);

  const toggle = useCallback(() => {
    if (audio.current) {
      stop();
      return;
    }
    const context = new AudioContext();
    const gain = context.createGain();
    gain.gain.setValueAtTime(0.0001, context.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.012, context.currentTime + 1.4);
    gain.connect(context.destination);
    const oscillators = [174, 261].map((frequency, index) => {
      const oscillator = context.createOscillator();
      oscillator.type = index === 0 ? "sine" : "triangle";
      oscillator.frequency.value = frequency;
      oscillator.detune.value = index === 0 ? -7 : 5;
      oscillator.connect(gain);
      oscillator.start();
      return oscillator;
    });
    audio.current = { context, oscillators };
    setEnabled(true);
  }, [stop]);

  useEffect(() => stop, [stop]);
  return [enabled, toggle] as const;
}
