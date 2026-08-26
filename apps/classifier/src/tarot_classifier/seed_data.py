"""Bundled bilingual seed corpus for the fixed intent taxonomy.

The corpus is deliberately small and explicit. It establishes the service boundary and
is not a substitute for reviewed production labels.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Iterator

from .taxonomy import INTENT_TO_DOMAIN


@dataclass(frozen=True)
class SeedExample:
    question: str
    intent: str
    locale: str


SEED_EXAMPLES: dict[str, dict[str, tuple[str, ...]]] = {
    "GENERAL_DAILY": {
        "en": (
            "What energy surrounds my day today?",
            "What should I know about today?",
            "Give me my daily tarot guidance.",
            "What is today's message for me?",
        ),
        "th": (
            "วันนี้มีพลังงานอะไรอยู่รอบตัวฉัน",
            "วันนี้ฉันควรรู้อะไร",
            "ขอคำแนะนำไพ่ประจำวัน",
            "ข้อความสำหรับวันนี้ของฉันคืออะไร",
        ),
    },
    "GENERAL_DECISION": {
        "en": (
            "How should I decide between my options?",
            "What choice is best for me?",
            "Should I say yes or no to this opportunity?",
            "Help me understand this difficult decision.",
        ),
        "th": (
            "ฉันควรตัดสินใจระหว่างตัวเลือกอย่างไร",
            "ทางเลือกไหนดีที่สุดสำหรับฉัน",
            "ฉันควรตอบตกลงหรือปฏิเสธโอกาสนี้",
            "ช่วยให้ฉันเข้าใจการตัดสินใจที่ยากนี้",
        ),
    },
    "GENERAL_DIRECTION": {
        "en": (
            "What direction should my life take next?",
            "What is the next path for me?",
            "Where is my life heading?",
            "I feel lost and need general direction.",
        ),
        "th": (
            "ชีวิตของฉันควรไปทางไหนต่อ",
            "เส้นทางต่อไปของฉันคืออะไร",
            "ชีวิตของฉันกำลังมุ่งไปทางไหน",
            "ฉันรู้สึกหลงทางและต้องการทิศทาง",
        ),
    },
    "LOVE_GENERAL": {
        "en": (
            "What should I know about my love life?",
            "What energy surrounds romance for me?",
            "Give me general guidance about love.",
            "What is coming in my romantic life?",
        ),
        "th": (
            "ฉันควรรู้อะไรเกี่ยวกับชีวิตรัก",
            "มีพลังงานอะไรอยู่รอบเรื่องความรักของฉัน",
            "ขอคำแนะนำทั่วไปเกี่ยวกับความรัก",
            "อะไรจะเข้ามาในชีวิตรักของฉัน",
        ),
    },
    "LOVE_SINGLE": {
        "en": (
            "What does love hold for me while I am single?",
            "Will I stop being single soon?",
            "How can I find love as a single person?",
            "What should a single person know about romance?",
        ),
        "th": (
            "ความรักของคนโสดอย่างฉันจะเป็นอย่างไร",
            "ฉันจะหายโสดเร็ว ๆ นี้ไหม",
            "คนโสดอย่างฉันจะพบความรักได้อย่างไร",
            "คนโสดควรรู้อะไรเกี่ยวกับความรัก",
        ),
    },
    "LOVE_RELATIONSHIP": {
        "en": (
            "Where is my current relationship going?",
            "How is the bond between me and my partner?",
            "What can improve our relationship?",
            "What is happening in my partnership?",
        ),
        "th": (
            "ความสัมพันธ์ปัจจุบันของฉันจะไปทางไหน",
            "สายสัมพันธ์ระหว่างฉันกับแฟนเป็นอย่างไร",
            "อะไรจะช่วยปรับปรุงความสัมพันธ์ของเรา",
            "เกิดอะไรขึ้นในความสัมพันธ์ของฉัน",
        ),
    },
    "LOVE_BREAKUP": {
        "en": (
            "How can I move on after this breakup?",
            "What does the end of this relationship mean?",
            "Why did our love end?",
            "What should I learn from being broken up with?",
        ),
        "th": (
            "ฉันจะก้าวต่อไปหลังเลิกราได้อย่างไร",
            "การจบความสัมพันธ์นี้หมายถึงอะไร",
            "ทำไมความรักของเราถึงจบลง",
            "ฉันควรเรียนรู้อะไรจากการถูกบอกเลิก",
        ),
    },
    "LOVE_RECONCILIATION": {
        "en": (
            "Will my ex and I get back together?",
            "Is reconciliation with my former partner possible?",
            "Can our past relationship be repaired?",
            "Should I reconnect with my ex?",
        ),
        "th": (
            "ฉันกับแฟนเก่าจะกลับมาคบกันไหม",
            "การคืนดีกับคนรักเก่าเป็นไปได้ไหม",
            "ความสัมพันธ์ในอดีตของเราซ่อมแซมได้ไหม",
            "ฉันควรกลับไปติดต่อแฟนเก่าไหม",
        ),
    },
    "LOVE_NEW_PERSON": {
        "en": (
            "Will someone new enter my love life?",
            "What should I know about the new person I met?",
            "Is a new romantic connection approaching?",
            "What energy does this new person bring?",
        ),
        "th": (
            "จะมีคนใหม่เข้ามาในชีวิตรักของฉันไหม",
            "ฉันควรรู้อะไรเกี่ยวกับคนใหม่ที่เพิ่งพบ",
            "ความสัมพันธ์ครั้งใหม่กำลังเข้ามาหรือไม่",
            "คนใหม่คนนี้นำพลังงานแบบใดมา",
        ),
    },
    "LOVE_COMMITMENT": {
        "en": (
            "Is this relationship ready for commitment?",
            "Will we get married?",
            "Should we take our relationship to the next level?",
            "Does my partner want a serious future together?",
        ),
        "th": (
            "ความสัมพันธ์นี้พร้อมสำหรับการผูกมัดไหม",
            "เราจะแต่งงานกันไหม",
            "เราควรพัฒนาความสัมพันธ์ไปอีกขั้นไหม",
            "แฟนของฉันต้องการอนาคตที่จริงจังด้วยกันไหม",
        ),
    },
    "LOVE_DECISION": {
        "en": (
            "How should I decide between two romantic paths?",
            "Should I stay in this relationship or leave?",
            "Which love choice is right for me?",
            "I need to make an important relationship decision.",
        ),
        "th": (
            "ฉันควรเลือกระหว่างสองเส้นทางความรักอย่างไร",
            "ฉันควรอยู่ในความสัมพันธ์นี้หรือจากไป",
            "ทางเลือกด้านความรักแบบไหนเหมาะกับฉัน",
            "ฉันต้องตัดสินใจเรื่องความสัมพันธ์ที่สำคัญ",
        ),
    },
    "CAREER_GENERAL": {
        "en": (
            "What should I know about my career?",
            "What energy surrounds my working life?",
            "Give me general guidance about work.",
            "What is ahead for my career path?",
        ),
        "th": (
            "ฉันควรรู้อะไรเกี่ยวกับอาชีพของฉัน",
            "มีพลังงานอะไรอยู่รอบชีวิตการทำงานของฉัน",
            "ขอคำแนะนำทั่วไปเกี่ยวกับการงาน",
            "เส้นทางอาชีพข้างหน้าของฉันเป็นอย่างไร",
        ),
    },
    "CAREER_NEW_JOB": {
        "en": (
            "Will I find a new job soon?",
            "How can I succeed in my job search?",
            "Is a new employment opportunity coming?",
            "What should I know about applying for a new position?",
        ),
        "th": (
            "ฉันจะได้งานใหม่เร็ว ๆ นี้ไหม",
            "ฉันจะประสบความสำเร็จในการหางานได้อย่างไร",
            "โอกาสการจ้างงานใหม่กำลังเข้ามาไหม",
            "ฉันควรรู้อะไรเกี่ยวกับการสมัครตำแหน่งใหม่",
        ),
    },
    "CAREER_CHANGE_JOB": {
        "en": (
            "Is changing careers the right move for me?",
            "Would leaving my current role improve my future?",
            "Is it time to switch jobs?",
            "Should I resign and work somewhere else?",
            "Would a job change be good for me?",
            "Should I leave this position for another company?",
        ),
        "th": (
            "การเปลี่ยนอาชีพเป็นทางเลือกที่เหมาะกับฉันไหม",
            "การออกจากตำแหน่งปัจจุบันจะทำให้อนาคตดีขึ้นไหม",
            "ถึงเวลาเปลี่ยนงานหรือยัง",
            "ฉันควรลาออกแล้วไปทำงานที่อื่นไหม",
            "การย้ายงานจะดีกับฉันหรือไม่",
            "ฉันควรออกจากตำแหน่งนี้ไปบริษัทอื่นไหม",
        ),
    },
    "CAREER_PROMOTION": {
        "en": (
            "Will I receive a promotion?",
            "How can I advance to a higher position?",
            "Is career advancement coming at work?",
            "Am I ready to ask for a promotion?",
        ),
        "th": (
            "ฉันจะได้รับการเลื่อนตำแหน่งไหม",
            "ฉันจะก้าวไปสู่ตำแหน่งที่สูงขึ้นได้อย่างไร",
            "ความก้าวหน้าในหน้าที่การงานกำลังมาไหม",
            "ฉันพร้อมขอเลื่อนตำแหน่งหรือยัง",
        ),
    },
    "CAREER_BUSINESS": {
        "en": (
            "What is ahead for my business venture?",
            "Should I start my own company?",
            "How can my business grow?",
            "Is entrepreneurship the right career for me?",
        ),
        "th": (
            "ธุรกิจของฉันข้างหน้าจะเป็นอย่างไร",
            "ฉันควรเริ่มบริษัทของตัวเองไหม",
            "ธุรกิจของฉันจะเติบโตได้อย่างไร",
            "การเป็นผู้ประกอบการเป็นอาชีพที่เหมาะกับฉันไหม",
        ),
    },
    "CAREER_DECISION": {
        "en": (
            "Which career option should I choose?",
            "How should I make this work decision?",
            "Should I accept or decline this role?",
            "I need direction on a career choice.",
        ),
        "th": (
            "ฉันควรเลือกทางเลือกอาชีพแบบไหน",
            "ฉันควรตัดสินใจเรื่องงานนี้อย่างไร",
            "ฉันควรรับหรือปฏิเสธตำแหน่งนี้",
            "ฉันต้องการทิศทางในการเลือกอาชีพ",
        ),
    },
    "CAREER_CONFLICT": {
        "en": (
            "How should I handle conflict with my coworker?",
            "What can resolve tension with my boss?",
            "Why is there so much workplace conflict?",
            "How do I deal with a difficult colleague?",
        ),
        "th": (
            "ฉันควรจัดการความขัดแย้งกับเพื่อนร่วมงานอย่างไร",
            "อะไรจะช่วยคลี่คลายความตึงเครียดกับหัวหน้า",
            "ทำไมที่ทำงานจึงมีความขัดแย้งมาก",
            "ฉันจะรับมือกับเพื่อนร่วมงานที่ยากได้อย่างไร",
        ),
    },
    "MONEY_GENERAL": {
        "en": (
            "What should I know about my finances?",
            "What energy surrounds my money situation?",
            "Give me general financial guidance.",
            "What is ahead for my financial life?",
        ),
        "th": (
            "ฉันควรรู้อะไรเกี่ยวกับการเงินของฉัน",
            "มีพลังงานอะไรอยู่รอบสถานการณ์เงินของฉัน",
            "ขอคำแนะนำทั่วไปด้านการเงิน",
            "ชีวิตทางการเงินข้างหน้าของฉันเป็นอย่างไร",
        ),
    },
    "MONEY_INCOME": {
        "en": (
            "Will my income increase?",
            "How can I earn more money?",
            "Is a better source of income coming?",
            "What should I know about my salary and earnings?",
        ),
        "th": (
            "รายได้ของฉันจะเพิ่มขึ้นไหม",
            "ฉันจะหาเงินได้มากขึ้นอย่างไร",
            "แหล่งรายได้ที่ดีกว่ากำลังเข้ามาไหม",
            "ฉันควรรู้อะไรเกี่ยวกับเงินเดือนและรายรับ",
        ),
    },
    "MONEY_INVESTMENT": {
        "en": (
            "Is this investment a wise choice?",
            "What should I know before investing?",
            "Will my investments grow?",
            "How should I approach the investment market?",
        ),
        "th": (
            "การลงทุนนี้เป็นทางเลือกที่ฉลาดไหม",
            "ฉันควรรู้อะไรก่อนลงทุน",
            "เงินลงทุนของฉันจะเติบโตไหม",
            "ฉันควรเข้าหาตลาดการลงทุนอย่างไร",
        ),
    },
    "MONEY_BUSINESS": {
        "en": (
            "Will this business make money?",
            "How can I improve my company's finances?",
            "What is the financial outlook for my business?",
            "Is this commercial opportunity profitable?",
        ),
        "th": (
            "ธุรกิจนี้จะทำเงินได้ไหม",
            "ฉันจะปรับปรุงการเงินของบริษัทได้อย่างไร",
            "แนวโน้มทางการเงินของธุรกิจเป็นอย่างไร",
            "โอกาสทางการค้านี้จะมีกำไรไหม",
        ),
    },
    "MONEY_DEBT": {
        "en": (
            "How can I get out of debt?",
            "Will I be able to repay what I owe?",
            "What should I do about my loans?",
            "How can I manage this debt burden?",
        ),
        "th": (
            "ฉันจะหลุดพ้นจากหนี้ได้อย่างไร",
            "ฉันจะสามารถชำระสิ่งที่ติดค้างได้ไหม",
            "ฉันควรทำอย่างไรกับเงินกู้",
            "ฉันจะจัดการภาระหนี้นี้ได้อย่างไร",
        ),
    },
    "MONEY_PURCHASE": {
        "en": (
            "Should I make this expensive purchase?",
            "Is now a good time to buy a house?",
            "Would buying this item be financially wise?",
            "Should I spend money on this major purchase?",
        ),
        "th": (
            "ฉันควรซื้อของราคาแพงชิ้นนี้ไหม",
            "ตอนนี้เป็นเวลาที่ดีในการซื้อบ้านไหม",
            "การซื้อของชิ้นนี้ฉลาดทางการเงินหรือไม่",
            "ฉันควรใช้เงินกับการซื้อครั้งใหญ่นี้ไหม",
        ),
    },
    "MONEY_DECISION": {
        "en": (
            "Which financial option should I choose?",
            "How should I make this money decision?",
            "Should I save or spend these funds?",
            "Help me choose between two financial paths.",
        ),
        "th": (
            "ฉันควรเลือกทางเลือกทางการเงินแบบไหน",
            "ฉันควรตัดสินใจเรื่องเงินนี้อย่างไร",
            "ฉันควรเก็บออมหรือใช้เงินก้อนนี้",
            "ช่วยฉันเลือกระหว่างสองเส้นทางทางการเงิน",
        ),
    },
    "FAMILY_GENERAL": {
        "en": (
            "What should I know about my family life?",
            "What energy surrounds my home and relatives?",
            "Give me general guidance about family.",
            "What is ahead for my household?",
        ),
        "th": (
            "ฉันควรรู้อะไรเกี่ยวกับชีวิตครอบครัว",
            "มีพลังงานอะไรอยู่รอบบ้านและญาติของฉัน",
            "ขอคำแนะนำทั่วไปเกี่ยวกับครอบครัว",
            "ครอบครัวของฉันข้างหน้าจะเป็นอย่างไร",
        ),
    },
    "FAMILY_CONFLICT": {
        "en": (
            "How can we resolve this family conflict?",
            "Why do my relatives keep arguing?",
            "How should I handle tension at home?",
            "What can heal a disagreement in my family?",
        ),
        "th": (
            "เราจะแก้ไขความขัดแย้งในครอบครัวได้อย่างไร",
            "ทำไมญาติของฉันจึงทะเลาะกันอยู่เรื่อย ๆ",
            "ฉันควรจัดการความตึงเครียดที่บ้านอย่างไร",
            "อะไรจะช่วยเยียวยาความไม่ลงรอยในครอบครัว",
        ),
    },
    "PERSONAL_GROWTH_GENERAL": {
        "en": (
            "How can I grow into a better version of myself?",
            "What lesson supports my personal development?",
            "What should I focus on for self improvement?",
            "Where am I on my inner growth journey?",
        ),
        "th": (
            "ฉันจะเติบโตเป็นตัวเองในแบบที่ดีขึ้นได้อย่างไร",
            "บทเรียนอะไรสนับสนุนการพัฒนาตัวเองของฉัน",
            "ฉันควรให้ความสำคัญกับอะไรเพื่อปรับปรุงตัวเอง",
            "ฉันอยู่ตรงไหนบนเส้นทางการเติบโตภายใน",
        ),
    },
    "PERSONAL_GROWTH_HEALING": {
        "en": (
            "How can I heal emotionally?",
            "What will help me recover from past pain?",
            "How do I release old emotional wounds?",
            "What does my healing journey need now?",
        ),
        "th": (
            "ฉันจะเยียวยาอารมณ์ของตัวเองได้อย่างไร",
            "อะไรจะช่วยให้ฉันฟื้นจากความเจ็บปวดในอดีต",
            "ฉันจะปล่อยบาดแผลทางใจเก่าได้อย่างไร",
            "เส้นทางการเยียวยาของฉันต้องการอะไรตอนนี้",
        ),
    },
}

_EN_AUGMENTATIONS = (
    "{question}",
    "Please guide me: {question}",
    "My tarot question is: {question}",
    "I need insight. {question}",
)
_TH_AUGMENTATIONS = (
    "{question}",
    "โปรดแนะนำฉัน {question}",
    "คำถามไพ่ของฉันคือ {question}",
    "ฉันต้องการคำชี้แนะ {question}",
)


def validate_seed_corpus() -> None:
    if set(SEED_EXAMPLES) != set(INTENT_TO_DOMAIN):
        missing = set(INTENT_TO_DOMAIN) - set(SEED_EXAMPLES)
        extra = set(SEED_EXAMPLES) - set(INTENT_TO_DOMAIN)
        raise ValueError(f"Seed taxonomy mismatch; missing={missing}, extra={extra}")

    for intent, localized in SEED_EXAMPLES.items():
        if not localized.get("en") or not localized.get("th"):
            raise ValueError(f"Intent {intent} requires English and Thai examples.")


def iter_training_examples() -> Iterator[SeedExample]:
    validate_seed_corpus()
    for intent, localized in SEED_EXAMPLES.items():
        for locale, questions in localized.items():
            templates = _EN_AUGMENTATIONS if locale == "en" else _TH_AUGMENTATIONS
            for question in questions:
                for template in templates:
                    yield SeedExample(template.format(question=question), intent, locale)
