"use client";

import { useFrame, useLoader } from "@react-three/fiber";
import { useEffect, useMemo, useRef, type MutableRefObject } from "react";
import { AnimationMixer, Mesh, type AnimationAction } from "three";
import { GLTFLoader, type GLTF } from "three/examples/jsm/loaders/GLTFLoader.js";
import type { ReadingPhase, ShuffleVisualStep } from "./use-tarot-reading-flow";

const MODEL_ROOT = "/models/destiny-shop/initial/";
const MODEL_URLS = ["shop", "advisor", "visitor", "tarot-back", "astrology-advisor"].map(name => `${MODEL_ROOT}${name}.glb`);

// Called only in the explicitly entered, dynamically imported Canvas. Nothing is preloaded on the landing page.
export function useShopModels() {
  const [shop, advisor, visitor, card, astrology] = useLoader(GLTFLoader, MODEL_URLS);
  return { shop, advisor, visitor, card, astrology };
}

function prepareScene(asset: GLTF) {
  const scene = asset.scene.clone(true);
  scene.traverse(object => {
    if (object instanceof Mesh) { object.castShadow = true; object.receiveShadow = true; }
  });
  return scene;
}

export function ShopModel({ asset }: { asset: GLTF }) {
  const scene = useMemo(() => prepareScene(asset), [asset]);
  return <primitive object={scene} dispose={null} />;
}

export function advisorAnimation(phase: ReadingPhase, shuffleStep: ShuffleVisualStep, welcoming: boolean) {
  if (phase === "SHUFFLING") return shuffleStep === "DEALING" ? "dealing" : "shuffling";
  if (phase === "RESOLVING") return "dealing";
  if (phase === "REVEALING") return "reveal";
  if (phase === "GENERATING") return "listening";
  if (phase === "COMPLETE") return "result";
  if (phase === "SELECTING") return "seated";
  return welcoming ? "greeting" : "idle";
}

export function CharacterModel({ asset, animation, moving, reduceMotion }: {
  asset: GLTF; animation: string; moving?: MutableRefObject<boolean>; reduceMotion: boolean;
}) {
  const scene = useMemo(() => prepareScene(asset), [asset]);
  const mixer = useMemo(() => new AnimationMixer(scene), [scene]);
  const active = useRef<AnimationAction | null>(null);
  useEffect(() => () => { mixer.stopAllAction(); mixer.uncacheRoot(scene); active.current = null; }, [mixer, scene]);
  useFrame((_, delta) => {
    const name = moving ? (moving.current && !reduceMotion ? "walk" : "idle") : animation;
    const clip = asset.animations.find(candidate => candidate.name === name) ?? asset.animations[0];
    if (!clip) return;
    const action = mixer.clipAction(clip);
    if (active.current !== action) {
      const previous = active.current;
      action.reset().setEffectiveTimeScale(name === "walk" ? 2.2 : 1).setEffectiveWeight(1).play();
      if (previous && !reduceMotion) action.crossFadeFrom(previous, .22, false);
      else previous?.stop();
      active.current = action;
      mixer.update(0);
    }
    if (!reduceMotion) mixer.update(Math.min(delta, .05));
  });
  return <primitive object={scene} dispose={null} />;
}
