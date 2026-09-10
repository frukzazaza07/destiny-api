"use client";

import {
  CapsuleCollider,
  CuboidCollider,
  Physics,
  RigidBody,
  type RapierCollider,
  type RapierRigidBody,
  useRapier,
} from "@react-three/rapier";
import { Canvas, useFrame, useThree } from "@react-three/fiber";
import { Component, Suspense, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Group, MathUtils, Raycaster, Vector3 } from "three";
import type { Locale } from "../lib/i18n";
import type { TarotReadingFlow } from "./use-tarot-reading-flow";
import { advisorAnimation, CharacterModel, ShopModel, useShopModels } from "./shop-models";
import TarotTable from "./tarot-table";

export type Movement = { x: number; z: number };
export type ShopZone = "ENTRANCE" | "GALLERY" | "TAROT_ROOM";
export type ShopInteraction = "RECEPTION" | "TAROT" | "THAI_ASTROLOGY" | "COMING_SOON" | null;
export type QualityProfile = "LOW" | "STANDARD" | "HIGH";

type DestinyShopCanvasProps = {
  locale: Locale;
  movement: Movement;
  reduceMotion: boolean;
  quality: QualityProfile;
  consultationOpen: boolean;
  astrologyOpen: boolean;
  astrologyState: string;
  onAstrology: () => void;
  flow: TarotReadingFlow;
  onInteractionChange: (interaction: ShopInteraction) => void;
  onZoneChange: (zone: ShopZone) => void;
  onInteract: () => void;
  onReady: () => void;
  onContextLost: () => void;
};

const cameraTarget = new Vector3();
const cameraIdeal = new Vector3();
const cameraDirection = new Vector3();
const movementVector = new Vector3();
const characterTranslation = new Vector3();

export default function DestinyShopCanvas(props: DestinyShopCanvasProps) {
  const dpr: [number, number] = props.quality === "LOW" ? [0.75, 1] : props.quality === "HIGH" ? [1, 2] : [1, 1.5];
  return (
    <div className="destiny-shop-canvas" data-testid="destiny-shop-canvas">
      <SceneFailureBoundary onFailure={props.onContextLost}>
      <Canvas
        shadows={props.quality !== "LOW"}
        dpr={dpr}
        camera={{ position: [0, 4.8, 15], fov: 51, near: 0.1, far: 70 }}
        gl={{ antialias: props.quality !== "LOW", alpha: false, powerPreference: "high-performance" }}
        fallback={<p>{props.locale === "th" ? "ไม่สามารถแสดงร้าน 3 มิติได้" : "The 3D shop is unavailable."}</p>}
        aria-label={props.locale === "th"
          ? "ร้านแห่งโชคชะตาสามมิติ มีโถงต้อนรับ แกลเลอรีบริการ และห้องไพ่ทาโรต์"
          : "A three-zone 3D destiny shop with reception, a service gallery, and a private Tarot room"}
      >
        <color attach="background" args={["#0e1517"]} />
        <fog attach="fog" args={["#11191a", 22, 49]} />
        <ContextLossHandler onContextLost={props.onContextLost} />
        <Suspense fallback={null}>
          <Physics gravity={[0, -18, 0]} timeStep="vary" colliders={false}>
            <ShopScene {...props} />
          </Physics>
        </Suspense>
      </Canvas>
      </SceneFailureBoundary>
    </div>
  );
}

function ContextLossHandler({ onContextLost }: { onContextLost: () => void }) {
  const { gl } = useThree();
  useEffect(() => {
    const canvas = gl.domElement;
    const lost = (event: Event) => { event.preventDefault(); onContextLost(); };
    canvas.addEventListener("webglcontextlost", lost);
    // R3F deliberately loses the old context after unmount. Its delayed event
    // must not close a newly entered shop.
    return () => canvas.removeEventListener("webglcontextlost", lost);
  }, [gl, onContextLost]);
  return null;
}

class SceneFailureBoundary extends Component<{ children: ReactNode; onFailure: () => void }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch() { this.props.onFailure(); }
  render() { return this.state.failed ? null : this.props.children; }
}

function ShopScene({
  movement, reduceMotion, quality, consultationOpen, astrologyOpen, astrologyState, onAstrology, flow, locale,
  onInteractionChange, onZoneChange, onInteract, onReady,
}: DestinyShopCanvasProps) {
  const { phase, shuffleVisualStep } = flow;
  const models = useShopModels();
  const [tableAnimation, setTableAnimation] = useState<string | null>(null);
  const playerBody = useRef<RapierRigidBody>(null);
  const playerCollider = useRef<RapierCollider>(null);
  const avatar = useRef<Group>(null);
  const walking = useRef(false);
  const pressedKeys = useRef(new Set<string>());
  const yaw = useRef(0);
  const pitch = useRef(0.28);
  const currentInteraction = useRef<ShopInteraction>(null);
  const currentZone = useRef<ShopZone>("ENTRANCE");
  const pointer = useRef({ active: false, x: 0, y: 0 });
  const raycaster = useMemo(() => new Raycaster(), []);
  const { world } = useRapier();
  const { camera, gl, scene, size } = useThree();
  const characterController = useRef<ReturnType<typeof world.createCharacterController> | null>(null);

  useEffect(() => {
    // Rapier controllers own native resources. Create and dispose them in the
    // same effect so Strict Mode replay and Fast Refresh never reuse a freed controller.
    const controller = world.createCharacterController(0.04);
    controller.setUp({ x: 0, y: 1, z: 0 });
    controller.enableAutostep(0.28, 0.12, true);
    controller.enableSnapToGround(0.25);
    characterController.current = controller;
    return () => {
      characterController.current = null;
      world.removeCharacterController(controller);
    };
  }, [world]);

  useEffect(() => onReady(), [onReady]);

  useEffect(() => {
    // A fresh scene starts at reception, including when returning after an exit.
    onZoneChange("ENTRANCE");
    onInteractionChange(null);
  }, [onZoneChange, onInteractionChange]);

  useEffect(() => {
    const movementKeys = new Set(["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "KeyW", "KeyA", "KeyS", "KeyD"]);
    const onKeyDown = (event: KeyboardEvent) => {
      if (consultationOpen || astrologyOpen) return;
      if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement || event.target instanceof HTMLSelectElement) return;
      if (movementKeys.has(event.code)) {
        event.preventDefault();
        pressedKeys.current.add(event.code);
      }
      if ((event.code === "KeyE" || event.code === "Enter") && currentInteraction.current) onInteract();
    };
    const onKeyUp = (event: KeyboardEvent) => pressedKeys.current.delete(event.code);
    const clearKeys = () => pressedKeys.current.clear();
    window.addEventListener("keydown", onKeyDown);
    window.addEventListener("keyup", onKeyUp);
    window.addEventListener("blur", clearKeys);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("keyup", onKeyUp);
      window.removeEventListener("blur", clearKeys);
    };
  }, [onInteract, onInteractionChange, consultationOpen, astrologyOpen]);

  useEffect(() => {
    pressedKeys.current.clear();
    pointer.current.active = false;
  }, [consultationOpen, astrologyOpen]);

  useEffect(() => {
    const canvas = gl.domElement;
    const pointerDown = (event: PointerEvent) => {
      if (event.pointerType === "touch" || consultationOpen || astrologyOpen) return;
      pointer.current = { active: true, x: event.clientX, y: event.clientY };
      canvas.setPointerCapture(event.pointerId);
    };
    const pointerMove = (event: PointerEvent) => {
      if (!pointer.current.active || consultationOpen || astrologyOpen) return;
      const dx = event.clientX - pointer.current.x;
      const dy = event.clientY - pointer.current.y;
      pointer.current.x = event.clientX;
      pointer.current.y = event.clientY;
      yaw.current -= dx * 0.0042;
      pitch.current = MathUtils.clamp(pitch.current + dy * 0.0027, 0.08, 0.62);
    };
    const pointerUp = () => { pointer.current.active = false; };
    canvas.addEventListener("pointerdown", pointerDown);
    canvas.addEventListener("pointermove", pointerMove);
    canvas.addEventListener("pointerup", pointerUp);
    canvas.addEventListener("pointercancel", pointerUp);
    return () => {
      canvas.removeEventListener("pointerdown", pointerDown);
      canvas.removeEventListener("pointermove", pointerMove);
      canvas.removeEventListener("pointerup", pointerUp);
      canvas.removeEventListener("pointercancel", pointerUp);
    };
  }, [consultationOpen, astrologyOpen, gl]);

  useFrame((_, rawDelta) => {
    const body = playerBody.current;
    const collider = playerCollider.current;
    const controller = characterController.current;
    if (!body || !collider || !controller) return;
    const delta = Math.min(rawDelta, 0.05);
    const keys = pressedKeys.current;
    let inputX = consultationOpen || astrologyOpen ? 0 : movement.x;
    let inputZ = consultationOpen || astrologyOpen ? 0 : movement.z;
    if (!consultationOpen && !astrologyOpen) {
      if (keys.has("ArrowLeft") || keys.has("KeyA")) inputX -= 1;
      if (keys.has("ArrowRight") || keys.has("KeyD")) inputX += 1;
      if (keys.has("ArrowUp") || keys.has("KeyW")) inputZ -= 1;
      if (keys.has("ArrowDown") || keys.has("KeyS")) inputZ += 1;
    }
    movementVector.set(inputX, 0, inputZ);
    walking.current = movementVector.lengthSq() > 0;
    if (movementVector.lengthSq() > 0) {
      movementVector.normalize();
      const forwardX = Math.sin(yaw.current);
      const forwardZ = -Math.cos(yaw.current);
      const rightX = Math.cos(yaw.current);
      const rightZ = Math.sin(yaw.current);
      characterTranslation.set(
        (rightX * movementVector.x - forwardX * movementVector.z) * delta * 4.6,
        -0.08,
        (rightZ * movementVector.x - forwardZ * movementVector.z) * delta * 4.6,
      );
      if (avatar.current) {
        const destinationRotation = Math.atan2(characterTranslation.x, characterTranslation.z);
        avatar.current.rotation.y = MathUtils.lerp(avatar.current.rotation.y, destinationRotation, reduceMotion ? 1 : 0.22);
      }
    } else {
      characterTranslation.set(0, -0.08, 0);
    }
    controller.computeColliderMovement(collider, characterTranslation);
    const allowed = controller.computedMovement();
    const translation = body.translation();
    body.setNextKinematicTranslation({ x: translation.x + allowed.x, y: Math.max(1, translation.y + allowed.y), z: translation.z + allowed.z });

    const zone: ShopZone = translation.z > 5 ? "ENTRANCE" : translation.z > -4.2 ? "GALLERY" : "TAROT_ROOM";
    if (zone !== currentZone.current) { currentZone.current = zone; onZoneChange(zone); }
    const receptionDistance = Math.hypot(translation.x + 4.9, translation.z - 7.8);
    const tarotDistance = Math.hypot(translation.x, translation.z + 7.1);
    const futureDistance = Math.min(Math.hypot(translation.x - 7.2, translation.z - 0.4), Math.hypot(translation.x + 7.2, translation.z - 0.4));
    const astrologyDistance = Math.hypot(translation.x + 5.5, translation.z + 1.8);
    const interaction: ShopInteraction = tarotDistance < 3.25 ? "TAROT" : astrologyDistance < 3 ? "THAI_ASTROLOGY" : receptionDistance < 2.7 ? "RECEPTION" : futureDistance < 2.6 ? "COMING_SOON" : null;
    if (interaction !== currentInteraction.current) { currentInteraction.current = interaction; onInteractionChange(interaction); }

    if (astrologyOpen) {
      cameraIdeal.set(-3, 4.2, 3.5); cameraTarget.set(-5.5, 1.4, -1.8);
      camera.position.lerp(cameraIdeal, reduceMotion ? 1 : 1 - Math.exp(-delta * 7)); camera.lookAt(cameraTarget); return;
    }
    if (consultationOpen) {
      cameraTarget.set(0, 1.35, -10.05);
      const framing = Math.max(1, .95 / (size.width / size.height));
      cameraIdeal.set(0, 1.35 + 4.6 * framing, -10.05 + 4.4 * framing);
      camera.position.lerp(cameraIdeal, reduceMotion ? 1 : 1 - Math.exp(-delta * 7));
      camera.lookAt(cameraTarget);
      return;
    }
    cameraTarget.set(translation.x, translation.y + 1.15, translation.z);
    const distance = 5.4;
    cameraIdeal.set(
      cameraTarget.x - Math.sin(yaw.current) * Math.cos(pitch.current) * distance,
      cameraTarget.y + Math.sin(pitch.current) * distance + 0.8,
      cameraTarget.z + Math.cos(yaw.current) * Math.cos(pitch.current) * distance,
    );
    cameraDirection.copy(cameraIdeal).sub(cameraTarget);
    const idealDistance = cameraDirection.length();
    cameraDirection.normalize();
    raycaster.set(cameraTarget, cameraDirection);
    raycaster.far = idealDistance;
    // Broadly raycasting the whole furnished shop would test tens of thousands of decorative triangles every frame.
    const cameraObstacles = scene.getObjectByName("Architecture_collision")?.children ?? [];
    const hit = raycaster.intersectObjects(cameraObstacles, false).find((entry) => entry.distance > 0.5);
    if (hit) cameraIdeal.copy(cameraTarget).addScaledVector(cameraDirection, Math.max(1.15, hit.distance - 0.25));
    camera.position.lerp(cameraIdeal, reduceMotion ? 1 : 1 - Math.exp(-delta * 10));
    camera.lookAt(cameraTarget);
  });

  const shadowMapSize = quality === "HIGH" ? 2048 : 1024;
  return (
    <>
      <ambientLight intensity={1.15} color="#e4d6c4" />
      <hemisphereLight args={["#c5dad6", "#594137", 1.5]} />
      <directionalLight castShadow={quality !== "LOW"} color="#ffdfaa" intensity={2.15} position={[7, 13, 8]} shadow-mapSize-width={shadowMapSize} shadow-mapSize-height={shadowMapSize} shadow-camera-far={42} shadow-camera-left={-17} shadow-camera-right={17} shadow-camera-top={16} shadow-camera-bottom={-16} />
      <ShopModel asset={models.shop} />
      <group position={[-5.5, 0, -1.8]} rotation={[0, .45, 0]} onClick={event => { event.stopPropagation(); onAstrology(); }}>
        <CharacterModel asset={models.astrology} animation={astrologyState === "COMPLETED" ? "result" : ["RUNNING", "QUEUED", "SUBMITTING"].includes(astrologyState) ? "listening" : astrologyOpen ? "greeting" : "idle"} reduceMotion={reduceMotion} />
      </group>
      <ShopColliders />
      {quality !== "LOW" && <pointLight position={[0, 3.8, -10]} color="#ffcf8a" intensity={12} distance={9} />}
      <group position={[0, -.24, -11.75]}>
        <CharacterModel asset={models.advisor} animation={tableAnimation ?? advisorAnimation(phase, shuffleVisualStep, consultationOpen || currentInteraction.current === "TAROT")} reduceMotion={reduceMotion} />
      </group>
      <TarotTable flow={flow} active={consultationOpen} reduceMotion={reduceMotion} locale={locale} onAnimation={setTableAnimation} />
      <RigidBody ref={playerBody} type="kinematicPosition" colliders={false} position={[0, 1, 11.1]} enabledRotations={[false, false, false]}>
        <CapsuleCollider ref={playerCollider} args={[0.52, 0.34]} friction={0} />
        <group ref={avatar} visible={!consultationOpen} position={[0, -.94, 0]} rotation={[0, Math.PI, 0]}>
          <CharacterModel asset={models.visitor} animation="idle" moving={walking} reduceMotion={reduceMotion} />
        </group>
      </RigidBody>
    </>
  );
}

function ShopColliders() {
  return <RigidBody type="fixed" colliders={false}>
    <CuboidCollider args={[16, .1, 14]} position={[0, -.1, 0]} />
    {[-16, 16].map(x => <CuboidCollider key={x} args={[.18, 2.4, 14]} position={[x, 2.4, 0]} />)}
    <CuboidCollider args={[16, 2.4, .18]} position={[0, 2.4, -14]} />
    <CuboidCollider args={[16, 2.4, .18]} position={[0, 2.4, 14]} />
    {[5.1, -4.3].flatMap(z => [-10.2, 10.2].map(x =>
      <CuboidCollider key={`${x}-${z}`} args={[5.8, 2.4, .15]} position={[x, 2.4, z]} />))}
    {[5.05, -4.35].flatMap(z => [-2.45, 2.45].map(x =>
      <CuboidCollider key={`post-${x}-${z}`} args={[.2, 2.3, .24]} position={[x, 2.3, z]} />))}
    <CuboidCollider args={[2.56, .76, .65]} position={[-4.9, .76, 7.8]} />
    <CuboidCollider args={[2.15, .53, 1.35]} position={[0, .53, -9.9]} />
    {[-11.85, -7.95].map(z => <CuboidCollider key={z} args={[.46, .7, .44]} position={[0, .7, z]} />)}
    {[-7.2, 7.2, -11.7, 11.7].map(x => <CuboidCollider key={x} args={[1.4, .9, 1.7]} position={[x, .9, Math.abs(x) > 10 ? -2.3 : -.1]} />)}
    {[-12.7, 12.7].map(x => <CuboidCollider key={x} args={[1.15, 1.5, .42]} position={[x, 1.5, -12.8]} />)}
    <CuboidCollider args={[1.85, .88, .55]} position={[8, .88, 10]} />
  </RigidBody>;
}
