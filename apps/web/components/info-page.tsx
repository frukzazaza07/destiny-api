import Link from "next/link";
import { localizedPath, type Locale } from "../lib/i18n";

export const INFO_SLUGS = [
  "about",
  "contact",
  "privacy",
  "terms",
  "cookie-policy",
  "disclaimer"
] as const;

export type InfoSlug = (typeof INFO_SLUGS)[number];

type InfoSection = {
  heading: string;
  paragraphs: readonly string[];
  bullets?: readonly string[];
};

type InfoDocument = {
  title: string;
  description: string;
  introduction: string;
  sections: readonly InfoSection[];
};

const documents: Record<Locale, Record<InfoSlug, InfoDocument>> = {
  en: {
    about: {
      title: "About Tarot Destiny",
      description: "How Tarot Destiny approaches tarot as a grounded tool for adult self-reflection.",
      introduction:
        "Tarot Destiny is a bilingual reading experience and learning library for adults who want to use tarot thoughtfully.",
      sections: [
        {
          heading: "Our approach",
          paragraphs: [
            "We treat tarot imagery as a structured prompt. A reading can help you notice feelings, assumptions, tensions, and possible next steps, but it cannot establish facts or determine the future.",
            "The person receiving a reading keeps responsibility for every decision. For important matters, use reliable evidence and speak with a suitably qualified professional."
          ]
        },
        {
          heading: "How the service works",
          paragraphs: [
            "You choose a spread, a focus, and cards from a shuffled deck. The service combines the selected cards with established symbolic themes to produce a reflective interpretation in Thai or English.",
            "Our guides explain the system in plain language and are reviewed before publication. Advertising, when enabled, is kept away from the reading tool and its results."
          ]
        },
        {
          heading: "Who it is for",
          paragraphs: [
            "The service is intended only for people aged 18 or over. It is for entertainment and self-reflection, not diagnosis, treatment, prediction, or professional advice."
          ]
        }
      ]
    },
    contact: {
      title: "Contact",
      description: "Contact Tarot Destiny about the service, privacy, accessibility, or published content.",
      introduction:
        "Use the contact channel below for service questions, privacy requests, accessibility feedback, corrections, or copyright concerns.",
      sections: [
        {
          heading: "What to include",
          paragraphs: [
            "Describe the page or issue clearly and include a reply address. Do not send reading questions, health records, financial account details, identity documents, passwords, or other sensitive information."
          ],
          bullets: [
            "The URL and language of the page",
            "A short description of the problem or request",
            "Any accessibility accommodation that would help you communicate with us"
          ]
        },
        {
          heading: "Response and emergencies",
          paragraphs: [
            "We aim to review legitimate messages, but we do not provide personal readings or professional advice through the contact channel.",
            "This service is not monitored for emergencies. If you or someone else may be in immediate danger, contact local emergency services or a qualified crisis service now."
          ]
        }
      ]
    },
    privacy: {
      title: "Privacy Policy",
      description: "How Tarot Destiny handles service data, analytics, advertising, cookies, and privacy choices.",
      introduction:
        "This policy explains the information processed when you visit Tarot Destiny. The final operator identity, contact details, and retention schedule must be reviewed and completed before production launch.",
      sections: [
        {
          heading: "Accounts and premium access",
          paragraphs: [
            "If you create or receive an account, we store the email address, a one-way password hash, verification and security status, active sessions, roles, and premium-entitlement history needed to operate and protect the account. We never store a plaintext password or attach reading questions and answers to advertising records.",
            "Disabling an account immediately revokes its sessions. Account and entitlement audit records may be retained for documented security, fraud-prevention, legal, and accounting periods. You can request access, correction, or deletion through the Contact page; some records may be retained where law or legitimate security needs require it."
          ]
        },
        {
          heading: "Information used to provide a reading",
          paragraphs: [
            "The service processes the locale, chosen spread, topic or question, and selected cards to generate a result. Reading inputs and results are operational service data; they are not sent to Google Analytics or Google advertising products.",
            "Technical logs may contain request time, route, status, coarse network information, and security signals needed to operate and protect the service. Do not enter names, account numbers, medical details, or other sensitive personal information in a reading question."
          ]
        },
        {
          heading: "Analytics",
          paragraphs: [
            "If analytics is enabled and you give the required consent, Google Analytics 4 receives public pageview information limited by our implementation to the public route and locale. We do not intentionally send reading questions, answers, selected topics or cards, user identifiers, authentication data, or admin activity.",
            "Google may process device, browser, network, cookie, and identifier information under its own terms. Analytics remains off when configuration or the required consent is missing."
          ]
        },
        {
          heading: "Rewarded DEEP access",
          paragraphs: [
            "If the feature is enabled and you consent to advertising, Google Ad Manager may show a rewarded ad only after your explicit request. We send no reading question, answer, selected card or topic, user ID, authentication token, or DEEP entitlement data to the advertising provider.",
            "The service keeps a minimal reward-session, one-time attempt, completion, and credit ledger for correctness and abuse prevention. Progress and unused credits expire after 24 hours. Withdrawing advertising consent blocks future ad requests but does not erase a credit already validly earned."
          ]
        },
        {
          heading: "Advertising",
          paragraphs: [
            "If advertising is enabled and you give the required consent, Google AdSense may serve non-personalized ads on eligible published guide pages. No ad is placed in the reading experience, reading result, legal pages, or admin area.",
            "Google and its partners may use cookies, local storage, IP-derived information, device information, and other identifiers to deliver, limit, secure, and measure ads. Non-personalized ads may still use contextual information and limited identifiers for functions such as frequency capping, fraud prevention, and reporting."
          ]
        },
        {
          heading: "Consent and your choices",
          paragraphs: [
            "Where consent is required, Google analytics and advertising scripts remain blocked until you make the corresponding affirmative choice. You can reopen Consent settings from every public-page footer to change or withdraw a choice. Withdrawal affects future processing and does not invalidate processing that was lawful before withdrawal.",
            "You can also limit cookies using your browser. Blocking required storage may affect preferences or service security. People in some jurisdictions may have rights to access, correct, delete, restrict, or object to certain processing, subject to applicable law and identity verification."
          ]
        },
        {
          heading: "Sharing, retention, and security",
          paragraphs: [
            "Information is shared only with infrastructure and service providers needed to host, secure, measure, or monetize the site, or when required by law. We do not claim that internet transmission or storage is completely secure.",
            "Data should be kept only for documented operational, security, legal, and accounting periods. The operator must approve and publish the final retention schedule before launch. The service is intended for adults aged 18 and over and is not directed to children."
          ]
        },
        {
          heading: "Google partner data",
          paragraphs: [
            "Google explains how it uses information from sites and apps that use its services on its partner-data page. That Google notice is separate from this policy."
          ]
        }
      ]
    },
    terms: {
      title: "Terms of Use",
      description: "The terms that apply when adults use Tarot Destiny readings and guides.",
      introduction:
        "By using Tarot Destiny, you agree to these terms. If you do not agree, do not use the service.",
      sections: [
        {
          heading: "Accounts and Premium DEEP",
          paragraphs: [
            "Account credentials are personal and must not be shared. Premium DEEP access is available only while the server records an active, unrevoked entitlement for an enabled and verified account; browser state does not grant access.",
            "We may suspend accounts, revoke sessions, or revoke access when needed to protect the service, enforce these terms, address abuse, or comply with law. Expiry and revocation take effect without requiring the browser to refresh its stored session."
          ]
        },
        {
          heading: "Eligibility and purpose",
          paragraphs: [
            "You must be at least 18 years old. The service is offered for personal entertainment, education, and self-reflection. It does not provide medical, mental-health, legal, financial, employment, relationship, or other professional advice."
          ]
        },
        {
          heading: "Your decisions",
          paragraphs: [
            "Readings are generated interpretations, may be incomplete or wrong, and cannot verify facts, another person's thoughts, or future events. You remain responsible for checking information and for every action or decision you take.",
            "Never delay urgent help, treatment, legal advice, or financial guidance because of this service."
          ]
        },
        {
          heading: "Optional rewarded DEEP credits",
          paragraphs: [
            "When available, each rewarded ad is a separate voluntary choice. Only the provider's completed-ad grant event counts; clicks, partial views, closes, skips, errors, and unavailable ads do not count. Never click an ad to seek a reward.",
            "A completed bundle grants the database-configured number of single-use DEEP credits, limited to one bundle per rolling 24 hours. Credits expire after 24 hours, are non-transferable, usable only in this service, and have no cash value. Premium access is checked first and never consumes an ad-earned credit. STANDARD readings remain available without participating."
          ]
        },
        {
          heading: "Acceptable use",
          paragraphs: ["You must use the service lawfully and must not interfere with its operation or other visitors."],
          bullets: [
            "Do not attempt to bypass access controls, rate limits, consent controls, or security measures.",
            "Do not submit unlawful, abusive, infringing, or malicious material.",
            "Do not scrape, resell, or reproduce substantial parts of the service except where law permits."
          ]
        },
        {
          heading: "Availability and third parties",
          paragraphs: [
            "Features may change, pause, or end, and the service may be unavailable. Links, analytics, advertising, and other third-party services have separate terms and privacy practices.",
            "To the extent permitted by applicable law, the service is provided without guarantees of accuracy, fitness for a particular purpose, uninterrupted availability, or a particular outcome. Nothing in these terms excludes rights or liability that cannot lawfully be excluded."
          ]
        },
        {
          heading: "Changes and contact",
          paragraphs: [
            "We may update these terms and will change the date shown on this page. Continued use after an update means the revised terms apply to later use. Contact us through the Contact page with questions."
          ]
        }
      ]
    },
    "cookie-policy": {
      title: "Cookie Policy",
      description: "The storage technologies Tarot Destiny uses and how visitors can control optional cookies.",
      introduction:
        "Cookies and similar technologies store or read small pieces of information on a browser or device. This policy describes their purposes on Tarot Destiny.",
      sections: [
        {
          heading: "Required storage",
          paragraphs: [
            "Strictly necessary storage may be used to deliver requested features, protect the service, balance traffic, preserve security state, and remember privacy choices. These functions do not depend on advertising consent."
          ]
        },
        {
          heading: "Analytics storage",
          paragraphs: [
            "When Google Analytics 4 is configured, enabled, and permitted by your choice, analytics cookies or identifiers may help measure public pageviews, route, and locale. Analytics does not intentionally receive reading questions, generated answers, selected topics or cards, or admin activity."
          ]
        },
        {
          heading: "Advertising storage",
          paragraphs: [
            "When Google AdSense is configured, enabled, and permitted by your choice, advertising cookies or identifiers may support non-personalized ads on published guide pages. Separately, Google Ad Manager may load rewarded inventory on the reading page only after explicit opt-in. Advertising storage may support delivery, frequency limits, fraud prevention, and aggregated reporting."
          ]
        },
        {
          heading: "Managing your choice",
          paragraphs: [
            "Use Consent settings in the footer to accept, refuse, or withdraw optional categories. Your browser can also delete or block storage. Optional Google scripts remain off where the site cannot determine an approved consent treatment or when the required consent is absent.",
            "Cookie names, providers, purposes, and durations must be checked against the live production deployment and finalized during legal review before monetization or analytics is enabled."
          ]
        }
      ]
    },
    disclaimer: {
      title: "Disclaimer",
      description: "Important boundaries for Tarot Destiny readings, guides, and generated content.",
      introduction:
        "Tarot Destiny offers symbolic interpretations for adult entertainment and self-reflection. It cannot know the future or replace facts, judgment, or qualified help.",
      sections: [
        {
          heading: "No professional advice",
          paragraphs: [
            "Nothing on this service is medical, mental-health, legal, financial, tax, investment, employment, relationship, safety, or other professional advice. Do not use a reading to diagnose a condition, choose treatment, make a trade, sign a contract, manage debt, assess danger, or decide another person's rights."
          ]
        },
        {
          heading: "Uncertainty and personal agency",
          paragraphs: [
            "Generated interpretations may be inaccurate, incomplete, repetitive, or unsuitable for your situation. Cards cannot verify another person's intentions, consent, loyalty, health, location, or conduct.",
            "Use a reading as one prompt among many. Seek evidence, communicate directly when safe, consider alternatives, and take responsibility for your decisions."
          ]
        },
        {
          heading: "Urgent situations",
          paragraphs: [
            "Do not use this service in an emergency or when someone may be at risk. Contact local emergency services, a qualified clinician, a licensed adviser, or another appropriate professional without delay."
          ]
        },
        {
          heading: "Age and wellbeing",
          paragraphs: [
            "This service is for adults aged 18 and over. Stop using it if readings increase distress, compulsive checking, or dependence, and consider speaking with someone you trust or a qualified professional."
          ]
        }
      ]
    }
  },
  th: {
    about: {
      title: "เกี่ยวกับ Tarot Destiny",
      description: "แนวทางของ Tarot Destiny ที่ใช้ไพ่ทาโรต์เป็นเครื่องมือทบทวนตนเองอย่างมีหลักยึดสำหรับผู้ใหญ่",
      introduction: "Tarot Destiny คือประสบการณ์อ่านไพ่และคลังความรู้สองภาษาสำหรับผู้ใหญ่ที่ต้องการใช้ไพ่ทาโรต์อย่างใคร่ครวญ",
      sections: [
        {
          heading: "แนวทางของเรา",
          paragraphs: [
            "เรามองภาพบนไพ่ทาโรต์เป็นคำชวนคิดที่มีโครงสร้าง การอ่านไพ่อาจช่วยให้สังเกตความรู้สึก สมมติฐาน ความตึงเครียด และก้าวถัดไป แต่ไม่สามารถยืนยันข้อเท็จจริงหรือกำหนดอนาคตได้",
            "ผู้รับคำอ่านยังคงเป็นผู้รับผิดชอบต่อทุกการตัดสินใจ สำหรับเรื่องสำคัญ ควรใช้ข้อมูลที่เชื่อถือได้และปรึกษาผู้เชี่ยวชาญที่มีคุณสมบัติเหมาะสม"
          ]
        },
        {
          heading: "บริการทำงานอย่างไร",
          paragraphs: [
            "คุณเลือกรูปแบบไพ่ เรื่องที่ต้องการทบทวน และไพ่จากสำรับที่สับแล้ว ระบบจะเชื่อมโยงไพ่ที่เลือกกับความหมายเชิงสัญลักษณ์เพื่อสร้างคำตีความภาษาไทยหรืออังกฤษ",
            "คู่มือของเราอธิบายระบบด้วยภาษาที่เข้าใจง่ายและต้องผ่านการตรวจทานก่อนเผยแพร่ หากเปิดโฆษณา โฆษณาจะไม่อยู่ในเครื่องมืออ่านไพ่และผลการอ่าน"
          ]
        },
        {
          heading: "บริการนี้เหมาะกับใคร",
          paragraphs: ["บริการนี้สำหรับผู้มีอายุ 18 ปีขึ้นไปเท่านั้น มีไว้เพื่อความบันเทิงและการทบทวนตนเอง ไม่ใช่การวินิจฉัย การรักษา การทำนาย หรือคำแนะนำจากผู้เชี่ยวชาญ"]
        }
      ]
    },
    contact: {
      title: "ติดต่อเรา",
      description: "ติดต่อ Tarot Destiny เกี่ยวกับบริการ ความเป็นส่วนตัว การเข้าถึง หรือเนื้อหาที่เผยแพร่",
      introduction: "ใช้ช่องทางด้านล่างสำหรับคำถามเกี่ยวกับบริการ คำขอด้านความเป็นส่วนตัว ข้อเสนอแนะด้านการเข้าถึง การแก้ไขเนื้อหา หรือประเด็นลิขสิทธิ์",
      sections: [
        {
          heading: "ข้อมูลที่ควรระบุ",
          paragraphs: ["อธิบายหน้าเว็บหรือปัญหาให้ชัดเจนและระบุช่องทางตอบกลับ โปรดอย่าส่งคำถามอ่านไพ่ เวชระเบียน ข้อมูลบัญชีการเงิน เอกสารยืนยันตัวตน รหัสผ่าน หรือข้อมูลละเอียดอ่อนอื่น"],
          bullets: ["URL และภาษาของหน้าเว็บ", "คำอธิบายปัญหาหรือคำขอสั้น ๆ", "วิธีอำนวยความสะดวกด้านการเข้าถึงที่ช่วยให้สื่อสารกับเราได้"]
        },
        {
          heading: "การตอบกลับและเหตุฉุกเฉิน",
          paragraphs: [
            "เราตั้งใจตรวจสอบข้อความที่สมเหตุสมผล แต่ไม่ให้คำอ่านส่วนบุคคลหรือคำแนะนำวิชาชีพผ่านช่องทางติดต่อ",
            "ช่องทางนี้ไม่ได้เฝ้าระวังเหตุฉุกเฉิน หากคุณหรือผู้อื่นอาจอยู่ในอันตรายทันที โปรดติดต่อบริการฉุกเฉินในพื้นที่หรือหน่วยช่วยเหลือวิกฤตที่เหมาะสม"
          ]
        }
      ]
    },
    privacy: {
      title: "นโยบายความเป็นส่วนตัว",
      description: "วิธีที่ Tarot Destiny จัดการข้อมูลบริการ การวิเคราะห์ โฆษณา คุกกี้ และตัวเลือกความเป็นส่วนตัว",
      introduction: "นโยบายนี้อธิบายข้อมูลที่อาจถูกประมวลผลเมื่อคุณเข้าชม Tarot Destiny ต้องตรวจทานและเติมข้อมูลผู้ให้บริการ ช่องทางติดต่อ และระยะเวลาเก็บข้อมูลให้ครบก่อนเปิดใช้งานจริง",
      sections: [
        {
          heading: "บัญชีและสิทธิ์พรีเมียม",
          paragraphs: [
            "หากคุณสร้างหรือได้รับบัญชี เราจะจัดเก็บอีเมล แฮชรหัสผ่านแบบย้อนกลับไม่ได้ สถานะการยืนยันและความปลอดภัย เซสชัน บทบาท และประวัติสิทธิ์พรีเมียมเท่าที่จำเป็นต่อการให้บริการและปกป้องบัญชี เราไม่เก็บรหัสผ่านแบบข้อความธรรมดา และไม่เชื่อมคำถามหรือคำตอบการอ่านไพ่กับข้อมูลโฆษณา",
            "การปิดบัญชีจะยกเลิกทุกเซสชันทันที บันทึกบัญชีและการตรวจสอบสิทธิ์อาจถูกเก็บตามระยะเวลาที่กำหนดเพื่อความปลอดภัย การป้องกันทุจริต กฎหมาย และบัญชี คุณขอเข้าถึง แก้ไข หรือลบข้อมูลได้ผ่านหน้าติดต่อ โดยข้อมูลบางส่วนอาจต้องเก็บไว้ตามกฎหมายหรือเหตุผลด้านความปลอดภัยที่ชอบด้วยกฎหมาย"
          ]
        },
        {
          heading: "ข้อมูลที่ใช้เพื่อสร้างคำอ่าน",
          paragraphs: [
            "ระบบประมวลผลภาษา รูปแบบไพ่ หัวข้อหรือคำถาม และไพ่ที่เลือกเพื่อสร้างผลการอ่าน ข้อมูลที่ใช้และผลการอ่านเป็นข้อมูลการให้บริการ และจะไม่ถูกส่งไปยัง Google Analytics หรือผลิตภัณฑ์โฆษณาของ Google",
            "บันทึกทางเทคนิคอาจมีเวลาคำขอ เส้นทาง สถานะ ข้อมูลเครือข่ายโดยคร่าว และสัญญาณความปลอดภัยที่จำเป็นต่อการให้บริการ โปรดอย่าใส่ชื่อ เลขบัญชี ข้อมูลสุขภาพ หรือข้อมูลส่วนบุคคลละเอียดอ่อนในคำถาม"
          ]
        },
        {
          heading: "การวิเคราะห์",
          paragraphs: [
            "หากเปิด Google Analytics 4 และคุณให้ความยินยอมที่จำเป็น ระบบจะส่งเฉพาะข้อมูลการเปิดหน้าสาธารณะ เส้นทาง และภาษาเท่าที่การติดตั้งของเรากำหนด เราไม่ตั้งใจส่งคำถาม คำตอบ หัวข้อหรือไพ่ที่เลือก ตัวระบุผู้ใช้ ข้อมูลยืนยันตัวตน หรือกิจกรรมผู้ดูแล",
            "Google อาจประมวลผลข้อมูลอุปกรณ์ เบราว์เซอร์ เครือข่าย คุกกี้ และตัวระบุตามข้อกำหนดของตน การวิเคราะห์จะยังปิดเมื่อการตั้งค่าหรือความยินยอมที่จำเป็นไม่ครบ"
          ]
        },
        {
          heading: "สิทธิ์ DEEP จากโฆษณาแบบให้รางวัล",
          paragraphs: [
            "หากเปิดฟีเจอร์และคุณยินยอมด้านโฆษณา Google Ad Manager อาจแสดงโฆษณาแบบให้รางวัลหลังจากคุณกดขอแต่ละครั้งเท่านั้น เราไม่ส่งคำถาม คำตอบ ไพ่หรือหัวข้อที่เลือก รหัสผู้ใช้ โทเค็นเข้าสู่ระบบ หรือข้อมูลสิทธิ์ DEEP ให้ผู้ให้บริการโฆษณา",
            "ระบบเก็บเฉพาะเซสชัน รหัสทดลองใช้ครั้งเดียว เหตุการณ์สำเร็จ และบัญชีเครดิตขั้นต่ำที่จำเป็นต่อความถูกต้องและการป้องกันทุจริต ความคืบหน้าและเครดิตที่ยังไม่ใช้หมดอายุใน 24 ชั่วโมง การถอนความยินยอมจะหยุดคำขอโฆษณาใหม่ แต่ไม่ลบเครดิตที่ได้รับอย่างถูกต้องแล้ว"
          ]
        },
        {
          heading: "โฆษณา",
          paragraphs: [
            "หากเปิดโฆษณาและคุณให้ความยินยอมที่จำเป็น Google AdSense อาจแสดงโฆษณาแบบไม่ปรับเฉพาะบุคคลบนหน้าคู่มือที่เผยแพร่และมีสิทธิ์เท่านั้น จะไม่มีโฆษณาในเครื่องมืออ่านไพ่ ผลการอ่าน หน้ากฎหมาย หรือพื้นที่ผู้ดูแล",
            "Google และพันธมิตรอาจใช้คุกกี้ พื้นที่จัดเก็บภายใน ข้อมูลจาก IP ข้อมูลอุปกรณ์ และตัวระบุอื่นเพื่อแสดง จำกัด ป้องกันทุจริต และวัดผลโฆษณา แม้เป็นโฆษณาแบบไม่ปรับเฉพาะบุคคลก็อาจใช้บริบทและตัวระบุแบบจำกัดเพื่อการทำงานดังกล่าว"
          ]
        },
        {
          heading: "ความยินยอมและทางเลือกของคุณ",
          paragraphs: [
            "เมื่อกฎหมายกำหนดให้ขอความยินยอม สคริปต์วิเคราะห์และโฆษณาของ Google จะถูกบล็อกจนกว่าคุณจะเลือกยอมรับหมวดนั้น คุณเปิด “ตั้งค่าความยินยอม” ที่ท้ายทุกหน้าสาธารณะเพื่อเปลี่ยนหรือถอนความยินยอมได้ การถอนมีผลต่อการประมวลผลในอนาคต",
            "คุณจำกัดคุกกี้ในเบราว์เซอร์ได้ การบล็อกพื้นที่จัดเก็บที่จำเป็นอาจกระทบการจำค่าหรือความปลอดภัย ผู้ใช้ในบางเขตอาจมีสิทธิ์เข้าถึง แก้ไข ลบ จำกัด หรือคัดค้านการประมวลผลตามกฎหมายที่ใช้บังคับ"
          ]
        },
        {
          heading: "การแบ่งปัน การเก็บรักษา และความปลอดภัย",
          paragraphs: [
            "ข้อมูลจะแบ่งปันกับผู้ให้บริการโครงสร้างพื้นฐานที่จำเป็นต่อการโฮสต์ ปกป้อง วัดผล หรือสร้างรายได้จากเว็บไซต์ หรือเมื่อกฎหมายกำหนดเท่านั้น เราไม่อาจรับรองว่าการส่งหรือจัดเก็บผ่านอินเทอร์เน็ตปลอดภัยสมบูรณ์",
            "ควรเก็บข้อมูลเฉพาะระยะเวลาที่กำหนดเพื่อการปฏิบัติงาน ความปลอดภัย กฎหมาย และบัญชี ผู้ให้บริการต้องอนุมัติและเผยแพร่ตารางระยะเวลาเก็บข้อมูลฉบับสุดท้ายก่อนเปิดใช้งาน บริการนี้สำหรับผู้มีอายุ 18 ปีขึ้นไปและไม่ได้มุ่งถึงเด็ก"
          ]
        },
        {
          heading: "ข้อมูลพันธมิตรของ Google",
          paragraphs: ["Google อธิบายวิธีใช้ข้อมูลจากเว็บไซต์และแอปที่ใช้บริการของ Google ไว้ในหน้าข้อมูลสำหรับพันธมิตร ซึ่งเป็นประกาศแยกจากนโยบายนี้"]
        }
      ]
    },
    terms: {
      title: "ข้อกำหนดการใช้งาน",
      description: "ข้อกำหนดสำหรับผู้ใหญ่ที่ใช้คำอ่านและคู่มือของ Tarot Destiny",
      introduction: "เมื่อใช้ Tarot Destiny ถือว่าคุณยอมรับข้อกำหนดนี้ หากไม่ยอมรับ โปรดอย่าใช้บริการ",
      sections: [
        {
          heading: "บัญชีและ Premium DEEP",
          paragraphs: [
            "ข้อมูลเข้าสู่ระบบเป็นข้อมูลส่วนบุคคลและห้ามแบ่งปัน สิทธิ์ Premium DEEP ใช้ได้เฉพาะเมื่อเซิร์ฟเวอร์พบสิทธิ์ที่ยังใช้งาน ไม่ถูกเพิกถอน และบัญชีเปิดใช้งานพร้อมยืนยันอีเมลแล้ว สถานะในเบราว์เซอร์ไม่สามารถให้สิทธิ์ได้เอง",
            "เราอาจระงับบัญชี ยกเลิกเซสชัน หรือเพิกถอนสิทธิ์เพื่อปกป้องบริการ บังคับใช้ข้อกำหนด จัดการการใช้งานในทางที่ผิด หรือปฏิบัติตามกฎหมาย การหมดอายุและการเพิกถอนมีผลทันทีโดยไม่ต้องรอให้เบราว์เซอร์รีเฟรชเซสชัน"
          ]
        },
        {
          heading: "คุณสมบัติและวัตถุประสงค์",
          paragraphs: ["คุณต้องมีอายุอย่างน้อย 18 ปี บริการนี้จัดทำเพื่อความบันเทิง การเรียนรู้ และการทบทวนตนเองส่วนบุคคล ไม่ใช่คำแนะนำทางการแพทย์ สุขภาพจิต กฎหมาย การเงิน การงาน ความสัมพันธ์ หรือวิชาชีพอื่น"]
        },
        {
          heading: "การตัดสินใจของคุณ",
          paragraphs: [
            "คำอ่านเป็นการตีความที่สร้างขึ้นและอาจไม่ครบถ้วนหรือผิดพลาด ไม่สามารถยืนยันข้อเท็จจริง ความคิดของบุคคลอื่น หรือเหตุการณ์ในอนาคต คุณต้องตรวจสอบข้อมูลและรับผิดชอบต่อทุกการกระทำและการตัดสินใจ",
            "อย่าชะลอความช่วยเหลือเร่งด่วน การรักษา คำปรึกษากฎหมาย หรือคำแนะนำการเงินเพราะบริการนี้"
          ]
        },
        {
          heading: "เครดิต DEEP จากโฆษณาแบบสมัครใจ",
          paragraphs: [
            "โฆษณาแบบให้รางวัลแต่ละรายการเป็นทางเลือกแยกกัน นับเฉพาะเหตุการณ์ที่ผู้ให้บริการยืนยันว่าดูสำเร็จ การคลิก ดูบางส่วน ปิด ข้าม ข้อผิดพลาด หรือไม่มีโฆษณาจะไม่นับ ห้ามคลิกโฆษณาเพื่อรับรางวัล",
            "เมื่อครบตามจำนวนที่ฐานข้อมูลกำหนด ระบบจะให้เครดิต DEEP แบบใช้ครั้งเดียว โดยจำกัดหนึ่งชุดต่อช่วงเวลา 24 ชั่วโมง เครดิตหมดอายุใน 24 ชั่วโมง โอนไม่ได้ ใช้ได้เฉพาะบริการนี้ และไม่มีมูลค่าเงินสด ระบบตรวจสิทธิ์พรีเมียมก่อนและไม่ใช้เครดิตโฆษณาของผู้ใช้พรีเมียม การอ่าน STANDARD ยังใช้ได้โดยไม่ต้องเข้าร่วม"
          ]
        },
        {
          heading: "การใช้งานที่ยอมรับได้",
          paragraphs: ["คุณต้องใช้บริการอย่างถูกกฎหมายและไม่รบกวนการทำงานของระบบหรือผู้ใช้รายอื่น"],
          bullets: [
            "ห้ามพยายามเลี่ยงการควบคุมสิทธิ์ ขีดจำกัด ความยินยอม หรือมาตรการความปลอดภัย",
            "ห้ามส่งข้อมูลผิดกฎหมาย คุกคาม ละเมิดสิทธิ์ หรือเป็นอันตราย",
            "ห้ามเก็บข้อมูล ขายต่อ หรือทำซ้ำเนื้อหาส่วนสำคัญ เว้นแต่กฎหมายอนุญาต"
          ]
        },
        {
          heading: "ความพร้อมใช้งานและบุคคลภายนอก",
          paragraphs: [
            "คุณสมบัติอาจเปลี่ยน หยุดชั่วคราว หรือยุติ และบริการอาจขัดข้อง ลิงก์ การวิเคราะห์ โฆษณา และบริการภายนอกอยู่ภายใต้ข้อกำหนดและแนวปฏิบัติความเป็นส่วนตัวของผู้ให้บริการนั้น",
            "เท่าที่กฎหมายอนุญาต บริการไม่มีการรับประกันความถูกต้อง ความเหมาะสม ความต่อเนื่อง หรือผลลัพธ์ใด ข้อกำหนดนี้ไม่ตัดสิทธิ์หรือความรับผิดที่กฎหมายห้ามตัดออก"
          ]
        },
        {
          heading: "การเปลี่ยนแปลงและการติดต่อ",
          paragraphs: ["เราอาจปรับข้อกำหนดและเปลี่ยนวันที่บนหน้านี้ การใช้ต่อหลังแก้ไขหมายถึงข้อกำหนดใหม่ใช้กับการใช้งานครั้งต่อไป หากมีคำถามให้ติดต่อผ่านหน้าติดต่อเรา"]
        }
      ]
    },
    "cookie-policy": {
      title: "นโยบายคุกกี้",
      description: "เทคโนโลยีจัดเก็บข้อมูลที่ Tarot Destiny ใช้และวิธีควบคุมคุกกี้ทางเลือก",
      introduction: "คุกกี้และเทคโนโลยีคล้ายกันใช้เก็บหรืออ่านข้อมูลขนาดเล็กบนเบราว์เซอร์หรืออุปกรณ์ นโยบายนี้อธิบายวัตถุประสงค์บน Tarot Destiny",
      sections: [
        {
          heading: "พื้นที่จัดเก็บที่จำเป็น",
          paragraphs: ["อาจใช้พื้นที่จัดเก็บที่จำเป็นอย่างยิ่งเพื่อให้บริการฟังก์ชันที่ขอ ปกป้องระบบ กระจายการใช้งาน รักษาสถานะความปลอดภัย และจดจำตัวเลือกความเป็นส่วนตัว การทำงานเหล่านี้ไม่ขึ้นกับความยินยอมโฆษณา"]
        },
        {
          heading: "พื้นที่จัดเก็บเพื่อการวิเคราะห์",
          paragraphs: ["เมื่อกำหนดค่าและเปิด Google Analytics 4 พร้อมได้รับความยินยอม คุกกี้หรือตัวระบุอาจช่วยวัดการเปิดหน้าสาธารณะ เส้นทาง และภาษา โดยไม่ตั้งใจรับคำถาม คำตอบ หัวข้อหรือไพ่ที่เลือก หรือกิจกรรมผู้ดูแล"]
        },
        {
          heading: "พื้นที่จัดเก็บเพื่อโฆษณา",
          paragraphs: ["เมื่อกำหนดค่า เปิดใช้งาน และได้รับความยินยอม Google AdSense อาจใช้พื้นที่จัดเก็บเพื่อโฆษณาบนหน้าคู่มือ ส่วน Google Ad Manager อาจโหลดโฆษณาแบบให้รางวัลบนหน้าอ่านไพ่หลังจากผู้ใช้เลือกขออย่างชัดเจนเท่านั้น พื้นที่จัดเก็บอาจใช้เพื่อส่งโฆษณา จำกัดความถี่ ป้องกันทุจริต และรายงานแบบรวม"]
        },
        {
          heading: "การจัดการตัวเลือก",
          paragraphs: [
            "ใช้ “ตั้งค่าความยินยอม” ที่ท้ายหน้าเพื่อยอมรับ ปฏิเสธ หรือถอนหมวดทางเลือก คุณลบหรือบล็อกพื้นที่จัดเก็บผ่านเบราว์เซอร์ได้ สคริปต์ Google ทางเลือกจะยังปิดในพื้นที่ที่ยังไม่มีแนวทางความยินยอมที่อนุมัติหรือเมื่อไม่ได้รับความยินยอม",
            "ต้องตรวจชื่อคุกกี้ ผู้ให้บริการ วัตถุประสงค์ และระยะเวลาจากระบบจริง และสรุปให้ครบในการตรวจทานกฎหมายก่อนเปิดการวิเคราะห์หรือสร้างรายได้"
          ]
        }
      ]
    },
    disclaimer: {
      title: "ข้อจำกัดความรับผิด",
      description: "ขอบเขตสำคัญของคำอ่าน คู่มือ และเนื้อหาที่สร้างโดย Tarot Destiny",
      introduction: "Tarot Destiny ให้คำตีความเชิงสัญลักษณ์เพื่อความบันเทิงและการทบทวนตนเองของผู้ใหญ่ ไม่สามารถรู้อนาคตหรือแทนที่ข้อเท็จจริง วิจารณญาณ และความช่วยเหลือจากผู้เชี่ยวชาญ",
      sections: [
        {
          heading: "ไม่ใช่คำแนะนำวิชาชีพ",
          paragraphs: ["ไม่มีสิ่งใดในบริการนี้เป็นคำแนะนำทางการแพทย์ สุขภาพจิต กฎหมาย การเงิน ภาษี การลงทุน การงาน ความสัมพันธ์ ความปลอดภัย หรือวิชาชีพอื่น อย่าใช้คำอ่านเพื่อวินิจฉัย เลือกการรักษา ซื้อขายสินทรัพย์ ลงนามสัญญา จัดการหนี้ ประเมินอันตราย หรือตัดสินสิทธิ์ของบุคคลอื่น"]
        },
        {
          heading: "ความไม่แน่นอนและสิทธิ์ในการตัดสินใจ",
          paragraphs: [
            "คำตีความที่สร้างขึ้นอาจคลาดเคลื่อน ไม่ครบ ซ้ำ หรือไม่เหมาะกับสถานการณ์ ไพ่ไม่สามารถยืนยันเจตนา ความยินยอม ความซื่อสัตย์ สุขภาพ ตำแหน่ง หรือพฤติกรรมของบุคคลอื่น",
            "ใช้คำอ่านเป็นเพียงหนึ่งคำชวนคิด ค้นหาหลักฐาน สื่อสารโดยตรงเมื่อปลอดภัย พิจารณาทางเลือก และรับผิดชอบการตัดสินใจของตน"
          ]
        },
        {
          heading: "สถานการณ์เร่งด่วน",
          paragraphs: ["อย่าใช้บริการนี้ในเหตุฉุกเฉินหรือเมื่อบุคคลอาจมีความเสี่ยง โปรดติดต่อบริการฉุกเฉินในพื้นที่ บุคลากรทางการแพทย์ ที่ปรึกษาที่มีใบอนุญาต หรือผู้เชี่ยวชาญที่เหมาะสมโดยไม่ชักช้า"]
        },
        {
          heading: "อายุและสุขภาวะ",
          paragraphs: ["บริการนี้สำหรับผู้มีอายุ 18 ปีขึ้นไป หยุดใช้หากคำอ่านเพิ่มความทุกข์ การตรวจซ้ำแบบควบคุมไม่ได้ หรือการพึ่งพา และพิจารณาพูดคุยกับคนที่ไว้ใจหรือผู้เชี่ยวชาญ"]
        }
      ]
    }
  }
};

export function isInfoSlug(value: string): value is InfoSlug {
  return (INFO_SLUGS as readonly string[]).includes(value);
}

export function getInfoDocument(locale: Locale, slug: InfoSlug): InfoDocument {
  return documents[locale][slug];
}

export default function InfoPage({ locale, slug }: { locale: Locale; slug: InfoSlug }) {
  const document = getInfoDocument(locale, slug);
  const configuredContactEmail = process.env.CONTACT_EMAIL?.trim() ?? "";
  const contactEmail = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(configuredContactEmail)
    ? configuredContactEmail
    : null;

  return (
    <main id="main-content" className="info-page">
      <article>
        <header>
          <p className="eyebrow">Tarot Destiny</p>
          <h1>{document.title}</h1>
          <p className="info-introduction">{document.introduction}</p>
          <p className="info-updated">
            {locale === "th" ? "ปรับปรุงล่าสุด: 3 กันยายน 2026" : "Last updated: September 3, 2026"}
          </p>
        </header>

        {slug === "contact" && (
          <section className={contactEmail ? "contact-channel" : "launch-blocker"} aria-labelledby="contact-channel-heading">
            <h2 id="contact-channel-heading">{locale === "th" ? "ช่องทางติดต่อ" : "Contact channel"}</h2>
            {contactEmail ? (
              <p><a href={`mailto:${contactEmail}`}>{contactEmail}</a></p>
            ) : (
              <p>
                {locale === "th"
                  ? "ผู้ให้บริการต้องกำหนด CONTACT_EMAIL ซึ่งเป็นอีเมลที่มีผู้ตรวจสอบจริงก่อนเปิดเว็บไซต์หรือสมัคร AdSense"
                  : "The operator must configure a monitored CONTACT_EMAIL before the site launches or applies to AdSense."}
              </p>
            )}
          </section>
        )}

        {document.sections.map((section) => (
          <section key={section.heading}>
            <h2>{section.heading}</h2>
            {section.paragraphs.map((paragraph) => <p key={paragraph}>{paragraph}</p>)}
            {section.bullets && (
              <ul>{section.bullets.map((bullet) => <li key={bullet}>{bullet}</li>)}</ul>
            )}
            {slug === "privacy" && section.heading === (locale === "th" ? "ข้อมูลพันธมิตรของ Google" : "Google partner data") && (
              <p>
                <a href="https://policies.google.com/technologies/partner-sites" rel="external noopener noreferrer">
                  {locale === "th" ? "Google ใช้ข้อมูลจากเว็บไซต์หรือแอปที่ใช้บริการของ Google อย่างไร" : "How Google uses information from sites or apps that use its services"}
                </a>
              </p>
            )}
          </section>
        ))}

        <nav className="info-related" aria-label={locale === "th" ? "ข้อมูลที่เกี่ยวข้อง" : "Related information"}>
          <Link href={localizedPath(locale, "/privacy")}>{locale === "th" ? "ความเป็นส่วนตัว" : "Privacy"}</Link>
          <Link href={localizedPath(locale, "/terms")}>{locale === "th" ? "ข้อกำหนด" : "Terms"}</Link>
          <Link href={localizedPath(locale, "/cookie-policy")}>{locale === "th" ? "นโยบายคุกกี้" : "Cookie Policy"}</Link>
          <Link href={localizedPath(locale, "/disclaimer")}>{locale === "th" ? "ข้อจำกัดความรับผิด" : "Disclaimer"}</Link>
        </nav>
      </article>
    </main>
  );
}
