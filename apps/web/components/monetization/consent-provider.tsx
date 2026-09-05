"use client";

import {
  createContext,
  type FormEvent,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
} from "react";
import { usePathname } from "next/navigation";

import {
  CONSENT_SETTINGS_EVENT,
  CONSENT_STORAGE_KEY,
  createStoredThailandConsent,
  initialConsentSnapshot,
  parseStoredThailandConsent,
  type ConsentSnapshot,
  type OptionalGoogleServices,
} from "../../lib/consent-preferences";
import type { PrivacyRegion } from "../../lib/privacy-region";
import { isMonetizableGuidePath } from "../../lib/monetization-validation";
import {
  initializeGoogleConsentDefaults,
  updateGoogleConsent,
  type GoogleConsentSelection,
} from "./google-browser";

export interface ConsentProviderProps {
  readonly children: ReactNode;
  readonly locale: "en" | "th";
  readonly region: PrivacyRegion;
  readonly services?: Partial<OptionalGoogleServices>;
  readonly privacyHref?: string;
  readonly cookiePolicyHref?: string;
  readonly googleCmpConfigured?: boolean;
  readonly reloadOnWithdrawal?: boolean;
}

interface ConsentContextValue extends ConsentSnapshot {
  readonly reportGoogleCmpConsent: (
    selection: GoogleConsentSelection | null,
  ) => void;
}

const deniedContext: ConsentContextValue = {
  ...initialConsentSnapshot("restricted"),
  reportGoogleCmpConsent: () => undefined,
};

const ConsentContext = createContext<ConsentContextValue>(deniedContext);

export function ConsentProvider({
  children,
  locale,
  region,
  services,
  privacyHref = `/${locale}/privacy`,
  cookiePolicyHref = `/${locale}/cookie-policy`,
  googleCmpConfigured = false,
  reloadOnWithdrawal = true,
}: ConsentProviderProps) {
  const pathname = usePathname();
  const analyticsAvailable = services?.analytics === true;
  const advertisingAvailable = services?.advertising === true;
  const anyServiceAvailable = analyticsAvailable || advertisingAvailable;
  const [consent, setConsent] = useState<ConsentSnapshot>(() =>
    initialConsentSnapshot(region),
  );
  const [settingsOpen, setSettingsOpen] = useState(false);

  const applySelection = useCallback(
    (
      selection: GoogleConsentSelection,
      source: ConsentSnapshot["source"],
    ): ConsentSnapshot => {
      const next = {
        region,
        ready: true,
        analytics: analyticsAvailable && selection.analytics,
        advertising: advertisingAvailable && selection.advertising,
        source,
      } satisfies ConsentSnapshot;

      if (anyServiceAvailable) {
        updateGoogleConsent(next);
      }
      setConsent(next);
      return next;
    },
    [advertisingAvailable, analyticsAvailable, anyServiceAvailable, region],
  );

  useEffect(() => {
    setSettingsOpen(false);

    if (region === "restricted") {
      setConsent(initialConsentSnapshot("restricted"));
      return;
    }

    if (anyServiceAvailable) {
      initializeGoogleConsentDefaults();
    }

    if (region === "eea") {
      setConsent(initialConsentSnapshot("eea"));
      return;
    }

    let stored = null;
    try {
      stored = parseStoredThailandConsent(
        window.localStorage.getItem(CONSENT_STORAGE_KEY),
      );
    } catch {
      // Storage can be unavailable in privacy modes. Consent remains in memory
      // for this page only, and no Google request is made before a choice.
    }

    if (stored) {
      applySelection(stored, "stored");
    } else {
      setConsent(initialConsentSnapshot("thailand"));
      if (anyServiceAvailable) setSettingsOpen(true);
    }
  }, [anyServiceAvailable, applySelection, region]);

  useEffect(() => {
    const openSettings = () => {
      if (
        region !== "eea" ||
        !googleCmpConfigured ||
        !isMonetizableGuidePath(pathname)
      ) {
        setSettingsOpen(true);
      }
    };
    const handleDelegatedClick = (event: MouseEvent) => {
      const element =
        event.target instanceof Element
          ? event.target.closest("[data-consent-settings]")
          : null;
      if (!element) return;

      event.preventDefault();
      window.dispatchEvent(new Event(CONSENT_SETTINGS_EVENT));
    };

    window.addEventListener(CONSENT_SETTINGS_EVENT, openSettings);
    document.addEventListener("click", handleDelegatedClick);
    return () => {
      window.removeEventListener(CONSENT_SETTINGS_EVENT, openSettings);
      document.removeEventListener("click", handleDelegatedClick);
    };
  }, [googleCmpConfigured, pathname, region]);

  useEffect(() => {
    const synchronizeTabs = (event: StorageEvent) => {
      if (region !== "thailand" || event.key !== CONSENT_STORAGE_KEY) {
        return;
      }

      const stored = parseStoredThailandConsent(event.newValue);
      if (!stored) {
        if (anyServiceAvailable) {
          updateGoogleConsent({ analytics: false, advertising: false });
        }
        setConsent(initialConsentSnapshot("thailand"));
        setSettingsOpen(anyServiceAvailable);
        return;
      }

      applySelection(stored, "stored");
    };

    window.addEventListener("storage", synchronizeTabs);
    return () => window.removeEventListener("storage", synchronizeTabs);
  }, [anyServiceAvailable, applySelection, region]);

  const saveThailandSelection = useCallback(
    (selection: GoogleConsentSelection) => {
      if (region !== "thailand") return;

      const normalized = {
        analytics: analyticsAvailable && selection.analytics,
        advertising: advertisingAvailable && selection.advertising,
      };
      const record = createStoredThailandConsent(
        normalized.analytics,
        normalized.advertising,
      );

      try {
        window.localStorage.setItem(
          CONSENT_STORAGE_KEY,
          JSON.stringify(record),
        );
      } catch {
        // A storage failure never upgrades consent. The in-memory decision is
        // applied only to the current document.
      }

      const isWithdrawal =
        (consent.analytics && !normalized.analytics) ||
        (consent.advertising && !normalized.advertising);
      applySelection(normalized, "choice");
      setSettingsOpen(false);

      // A clean reload removes already-loaded vendor scripts after revocation.
      if (isWithdrawal && reloadOnWithdrawal) {
        window.setTimeout(() => window.location.reload(), 0);
      }
    },
    [
      advertisingAvailable,
      analyticsAvailable,
      applySelection,
      consent.advertising,
      consent.analytics,
      region,
      reloadOnWithdrawal,
    ],
  );

  const reportGoogleCmpConsent = useCallback(
    (selection: GoogleConsentSelection | null) => {
      if (region !== "eea") return;
      applySelection(
        selection ?? { analytics: false, advertising: false },
        "google-cmp",
      );
    },
    [applySelection, region],
  );

  const context = useMemo<ConsentContextValue>(
    () => ({ ...consent, reportGoogleCmpConsent }),
    [consent, reportGoogleCmpConsent],
  );

  return (
    <ConsentContext.Provider value={context}>
      {children}
      {settingsOpen && region === "thailand" ? (
        <ThailandConsentDialog
          consent={consent}
          locale={locale}
          services={{
            analytics: analyticsAvailable,
            advertising: advertisingAvailable,
          }}
          privacyHref={privacyHref}
          cookiePolicyHref={cookiePolicyHref}
          onClose={
            consent.ready || !anyServiceAvailable
              ? () => setSettingsOpen(false)
              : undefined
          }
          onSave={saveThailandSelection}
        />
      ) : null}
      {settingsOpen && region === "restricted" ? (
        <RestrictedConsentDialog
          locale={locale}
          onClose={() => setSettingsOpen(false)}
        />
      ) : null}
      {settingsOpen && region === "eea" ? (
        <EeaConsentInfoDialog
          configured={googleCmpConfigured}
          guidesHref={`/${locale}/guides`}
          locale={locale}
          onClose={() => setSettingsOpen(false)}
        />
      ) : null}
    </ConsentContext.Provider>
  );
}

export function useConsent(): ConsentContextValue {
  return useContext(ConsentContext);
}

interface ThailandConsentDialogProps {
  readonly consent: ConsentSnapshot;
  readonly locale: "en" | "th";
  readonly services: OptionalGoogleServices;
  readonly privacyHref: string;
  readonly cookiePolicyHref: string;
  readonly onClose?: () => void;
  readonly onSave: (selection: GoogleConsentSelection) => void;
}

function ThailandConsentDialog({
  consent,
  locale,
  services,
  privacyHref,
  cookiePolicyHref,
  onClose,
  onSave,
}: ThailandConsentDialogProps) {
  const copy = THAILAND_COPY[locale];
  const analyticsId = useId();
  const advertisingId = useId();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const [analytics, setAnalytics] = useState(
    services.analytics && consent.analytics,
  );
  const [advertising, setAdvertising] = useState(
    services.advertising && consent.advertising,
  );

  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  useEffect(() => {
    const handleEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose?.();
    };
    window.addEventListener("keydown", handleEscape);
    return () => window.removeEventListener("keydown", handleEscape);
  }, [onClose]);

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    onSave({ analytics, advertising });
  };

  return (
    <div style={styles.backdrop}>
      <section
        aria-labelledby="privacy-consent-heading"
        aria-modal="true"
        role="dialog"
        style={styles.dialog}
      >
        <h2
          id="privacy-consent-heading"
          ref={headingRef}
          style={styles.heading}
          tabIndex={-1}
        >
          {copy.heading}
        </h2>
        <p style={styles.body}>{copy.introduction}</p>
        {!services.analytics && !services.advertising ? (
          <p style={styles.notice}>{copy.servicesDisabled}</p>
        ) : (
          <form onSubmit={submit}>
            <label htmlFor={analyticsId} style={styles.choice}>
              <input
                checked={analytics}
                disabled={!services.analytics}
                id={analyticsId}
                onChange={(event) => setAnalytics(event.target.checked)}
                type="checkbox"
              />
              <span>
                <strong>{copy.analyticsHeading}</strong>
                <small style={styles.small}>{copy.analyticsDescription}</small>
              </span>
            </label>
            <label htmlFor={advertisingId} style={styles.choice}>
              <input
                checked={advertising}
                disabled={!services.advertising}
                id={advertisingId}
                onChange={(event) => setAdvertising(event.target.checked)}
                type="checkbox"
              />
              <span>
                <strong>{copy.advertisingHeading}</strong>
                <small style={styles.small}>{copy.advertisingDescription}</small>
              </span>
            </label>
            <div style={styles.actions}>
              <button
                onClick={() =>
                  onSave({ analytics: false, advertising: false })
                }
                style={styles.secondaryButton}
                type="button"
              >
                {copy.decline}
              </button>
              <button style={styles.primaryButton} type="submit">
                {copy.save}
              </button>
              <button
                onClick={() =>
                  onSave({
                    analytics: services.analytics,
                    advertising: services.advertising,
                  })
                }
                style={styles.secondaryButton}
                type="button"
              >
                {copy.allowAvailable}
              </button>
            </div>
          </form>
        )}
        <p style={styles.links}>
          <a href={privacyHref}>{copy.privacy}</a>
          <span aria-hidden="true"> · </span>
          <a href={cookiePolicyHref}>{copy.cookies}</a>
        </p>
        {onClose ? (
          <button onClick={onClose} style={styles.closeButton} type="button">
            {copy.close}
          </button>
        ) : null}
      </section>
    </div>
  );
}

function RestrictedConsentDialog({
  locale,
  onClose,
}: {
  readonly locale: "en" | "th";
  readonly onClose: () => void;
}) {
  const copy = RESTRICTED_COPY[locale];
  return (
    <div style={styles.backdrop}>
      <section aria-modal="true" role="dialog" style={styles.dialog}>
        <h2 style={styles.heading}>{copy.heading}</h2>
        <p style={styles.body}>{copy.body}</p>
        <div style={styles.actions}>
          <button onClick={onClose} style={styles.primaryButton} type="button">
            {copy.close}
          </button>
        </div>
      </section>
    </div>
  );
}

function EeaConsentInfoDialog({
  configured,
  guidesHref,
  locale,
  onClose
}: {
  readonly configured: boolean;
  readonly guidesHref: string;
  readonly locale: "en" | "th";
  readonly onClose: () => void;
}) {
  const copy = EEA_INFO_COPY[locale];
  return (
    <div style={styles.backdrop}>
      <section aria-modal="true" role="dialog" style={styles.dialog}>
        <h2 style={styles.heading}>{copy.heading}</h2>
        <p style={styles.body}>
          {configured ? copy.guideOnly : copy.servicesDisabled}
        </p>
        <div style={styles.actions}>
          {configured ? <a href={guidesHref}>{copy.openGuides}</a> : null}
          <button onClick={onClose} style={styles.primaryButton} type="button">
            {copy.close}
          </button>
        </div>
      </section>
    </div>
  );
}

const THAILAND_COPY = {
  en: {
    heading: "Privacy choices",
    introduction:
      "Optional Google services are off until you choose. Analytics measures page visits. Advertising displays non-personalized ads and may use cookies for frequency limits and aggregated reporting. You can change these choices at any time.",
    analyticsHeading: "Analytics",
    analyticsDescription:
      "Allow Google Analytics 4 to record this site's public route and language. Reading questions, cards, answers, and account data are never sent.",
    advertisingHeading: "Advertising",
    advertisingDescription:
      "Allow non-personalized Google ads on eligible guide pages and optional Google Ad Manager rewarded ads that load only when you explicitly request one in the reading tool.",
    servicesDisabled: "Optional Google services are not enabled on this site.",
    decline: "Decline optional",
    save: "Save choices",
    allowAvailable: "Allow available",
    privacy: "Privacy policy",
    cookies: "Cookie policy",
    close: "Close",
  },
  th: {
    heading: "ตัวเลือกความเป็นส่วนตัว",
    introduction:
      "บริการเสริมของ Google จะปิดอยู่จนกว่าคุณจะเลือก Analytics ใช้วัดการเข้าชมหน้าเว็บ ส่วนโฆษณาจะแสดงโฆษณาที่ไม่ได้ปรับตามโปรไฟล์และอาจใช้คุกกี้เพื่อจำกัดความถี่และจัดทำรายงานแบบรวม คุณเปลี่ยนตัวเลือกได้ทุกเมื่อ",
    analyticsHeading: "Analytics",
    analyticsDescription:
      "อนุญาตให้ Google Analytics 4 บันทึกเฉพาะเส้นทางหน้าสาธารณะและภาษาของเว็บไซต์ เราไม่ส่งคำถาม ไพ่ คำอ่าน หรือข้อมูลบัญชีของคุณ",
    advertisingHeading: "โฆษณา",
    advertisingDescription:
      "อนุญาตโฆษณา Google แบบไม่ปรับตามโปรไฟล์ในหน้าคู่มือ และโฆษณาแบบให้รางวัลของ Google Ad Manager ซึ่งโหลดเฉพาะเมื่อคุณกดขอในเครื่องมืออ่านไพ่แต่ละครั้ง",
    servicesDisabled: "ขณะนี้เว็บไซต์ไม่ได้เปิดใช้บริการเสริมของ Google",
    decline: "ปฏิเสธบริการเสริม",
    save: "บันทึกตัวเลือก",
    allowAvailable: "อนุญาตบริการที่เปิดใช้",
    privacy: "นโยบายความเป็นส่วนตัว",
    cookies: "นโยบายคุกกี้",
    close: "ปิด",
  },
} as const;

const RESTRICTED_COPY = {
  en: {
    heading: "Privacy settings",
    body: "Optional analytics and advertising services are unavailable for this region until its consent treatment has been reviewed.",
    close: "Close",
  },
  th: {
    heading: "การตั้งค่าความเป็นส่วนตัว",
    body: "บริการวิเคราะห์และโฆษณาเสริมยังไม่เปิดใช้สำหรับภูมิภาคนี้จนกว่าจะตรวจสอบแนวทางการขอความยินยอมแล้ว",
    close: "ปิด",
  },
} as const;

const EEA_INFO_COPY = {
  en: {
    heading: "Privacy and cookie settings",
    guideOnly:
      "Google's certified consent panel loads only on published guide pages, where optional measurement or advertising can occur. Open a guide and use this footer control there to review or withdraw your choice.",
    servicesDisabled:
      "Optional Google measurement and advertising are currently disabled, so there is no Google consent choice to change.",
    openGuides: "Open guides",
    close: "Close"
  },
  th: {
    heading: "การตั้งค่าความเป็นส่วนตัวและคุกกี้",
    guideOnly:
      "แผงความยินยอมที่ได้รับการรับรองจาก Google จะโหลดเฉพาะในหน้าคู่มือที่เผยแพร่ ซึ่งเป็นหน้าที่อาจมีการวัดผลหรือโฆษณา โปรดเปิดคู่มือและใช้ปุ่มตั้งค่านี้ที่ท้ายหน้าเพื่อตรวจสอบหรือถอนความยินยอม",
    servicesDisabled:
      "ขณะนี้การวัดผลและโฆษณาเสริมของ Google ปิดอยู่ จึงไม่มีตัวเลือกความยินยอมของ Google ที่ต้องเปลี่ยนแปลง",
    openGuides: "เปิดหน้าคู่มือ",
    close: "ปิด"
  }
} as const;

const styles = {
  backdrop: {
    position: "fixed",
    inset: 0,
    zIndex: 10000,
    display: "grid",
    alignItems: "end",
    padding: "1rem",
    background: "rgba(13, 9, 24, 0.62)",
  },
  dialog: {
    position: "relative",
    width: "min(100%, 46rem)",
    maxHeight: "calc(100vh - 2rem)",
    margin: "0 auto",
    overflow: "auto",
    border: "1px solid rgba(122, 92, 170, 0.3)",
    borderRadius: "1rem",
    padding: "1.25rem",
    color: "#241a31",
    background: "#fffdf9",
    boxShadow: "0 1rem 4rem rgba(13, 9, 24, 0.34)",
  },
  heading: { margin: "0 0 0.65rem", fontSize: "1.35rem" },
  body: { margin: "0 0 1rem", lineHeight: 1.6 },
  notice: {
    padding: "0.8rem",
    borderRadius: "0.6rem",
    background: "#f3eef8",
  },
  choice: {
    display: "grid",
    gridTemplateColumns: "1.25rem 1fr",
    gap: "0.75rem",
    alignItems: "start",
    margin: "0.8rem 0",
    padding: "0.8rem",
    border: "1px solid #ded5e8",
    borderRadius: "0.65rem",
    cursor: "pointer",
  },
  small: {
    display: "block",
    marginTop: "0.25rem",
    color: "#5e5369",
    lineHeight: 1.45,
  },
  actions: {
    display: "flex",
    flexWrap: "wrap",
    gap: "0.6rem",
    marginTop: "1rem",
  },
  primaryButton: {
    minHeight: "2.75rem",
    border: "1px solid #4b2d6d",
    borderRadius: "0.55rem",
    padding: "0.65rem 1rem",
    color: "white",
    background: "#4b2d6d",
    cursor: "pointer",
  },
  secondaryButton: {
    minHeight: "2.75rem",
    border: "1px solid #4b2d6d",
    borderRadius: "0.55rem",
    padding: "0.65rem 1rem",
    color: "#4b2d6d",
    background: "transparent",
    cursor: "pointer",
  },
  links: { margin: "1rem 0 0", fontSize: "0.9rem" },
  closeButton: {
    position: "absolute",
    top: "0.65rem",
    right: "0.65rem",
    border: 0,
    padding: "0.45rem",
    color: "#4b2d6d",
    background: "transparent",
    cursor: "pointer",
  },
} as const;
