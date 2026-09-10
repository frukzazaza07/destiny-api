export const LOCALES = ["th", "en"] as const;

export type Locale = (typeof LOCALES)[number];

export const astrologyCopy = {
  en: {
    title: "Thai astrology consultation", open: "Thai astrology · Open", meet: "Meet the astrology advisor",
    intro: "Explore your question with a Thai astrology advisor. Precise chart calculations are unavailable; this reading offers reflection and clearly explains its limits.",
    privacy: "Your birth details and question are sent to our configured AI provider for this consultation. Time and birthplace are optional; no location or timezone is assumed.",
    access: "This service requires DEEP access or one earned DEEP credit.", details: "Birth details and question",
    birthDate: "Birthdate (required, Gregorian calendar)", birthTime: "Birth time (optional, 24-hour)",
    birthPlace: "Birthplace (optional)", unknown: "Birth time unknown", question: "Your question (required)",
    submit: "Request astrology reading", retry: "Retry astrology reading", cancel: "Cancel astrology reading", restart: "New astrology reading",
    error: "The reading could not be completed. Check your DEEP access and connection, then retry. Your form is preserved.",
    states: { FORM: "", SUBMITTING: "Submitting consultation…", QUEUED: "Your consultation is queued…", RUNNING: "The advisor is considering your question…", COMPLETED: "Your astrology reading is ready.", FAILED: "Consultation failed.", CANCELED: "Consultation canceled." },
    sections: { overview: "Overview", analysis: "Analysis", directAnswer: "Answer to your question", timing: "Timing", advice: "Advice", dataLimitations: "Data limitations" },
  },
  th: {
    title: "ปรึกษาโหราศาสตร์ไทย", open: "โหราศาสตร์ไทย · เปิด", meet: "พบที่ปรึกษาโหราศาสตร์ไทย",
    intro: "สำรวจคำถามกับที่ปรึกษาโหราศาสตร์ไทย ขณะนี้ยังไม่มีการคำนวณผังดวงอย่างละเอียด คำอ่านนี้ช่วยสะท้อนมุมมองและอธิบายข้อจำกัดอย่างชัดเจน",
    privacy: "ข้อมูลเกิดและคำถามจะส่งให้ผู้ให้บริการ AI ที่กำหนดเพื่อการปรึกษานี้ เวลาและสถานที่เกิดไม่บังคับ ระบบไม่สมมติสถานที่หรือเขตเวลา",
    access: "บริการนี้ต้องมีสิทธิ์ DEEP หรือเครดิต DEEP ที่ได้รับ 1 เครดิต", details: "ข้อมูลเกิดและคำถาม",
    birthDate: "วันเกิด (จำเป็น, คริสต์ศักราช ค.ศ.)", birthTime: "เวลาเกิด (ไม่บังคับ, 24 ชั่วโมง)",
    birthPlace: "สถานที่เกิด (ไม่บังคับ)", unknown: "ไม่ทราบเวลาเกิด", question: "คำถามของคุณ (จำเป็น)",
    submit: "ขอคำอ่านโหราศาสตร์", retry: "ลองขอคำอ่านอีกครั้ง", cancel: "ยกเลิกคำอ่านโหราศาสตร์", restart: "เริ่มคำอ่านโหราศาสตร์ใหม่",
    error: "ไม่สามารถสร้างคำอ่านได้ โปรดตรวจสอบสิทธิ์ DEEP และการเชื่อมต่อแล้วลองใหม่ ข้อมูลในแบบฟอร์มยังอยู่",
    states: { FORM: "", SUBMITTING: "กำลังส่งคำขอ…", QUEUED: "คำปรึกษาของคุณอยู่ในคิว…", RUNNING: "ที่ปรึกษากำลังพิจารณาคำถามของคุณ…", COMPLETED: "คำอ่านโหราศาสตร์พร้อมแล้ว", FAILED: "การปรึกษาไม่สำเร็จ", CANCELED: "ยกเลิกการปรึกษาแล้ว" },
    sections: { overview: "ภาพรวม", analysis: "การวิเคราะห์", directAnswer: "คำตอบต่อคำถาม", timing: "ช่วงเวลา", advice: "คำแนะนำ", dataLimitations: "ข้อจำกัดของข้อมูล" },
  },
} as const;

export const DEFAULT_LOCALE: Locale = "th";

export function isLocale(value: string): value is Locale {
  return (LOCALES as readonly string[]).includes(value);
}

export function localizedPath(locale: Locale, path = ""): string {
  const normalizedPath = path === "/" ? "" : path.startsWith("/") ? path : `/${path}`;
  return normalizedPath ? `/${locale}${normalizedPath}` : `/${locale}/`;
}

export function otherLocale(locale: Locale): Locale {
  return locale === "th" ? "en" : "th";
}
