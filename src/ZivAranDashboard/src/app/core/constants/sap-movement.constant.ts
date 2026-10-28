// SAP Movement Category (BEWTY) + Type (BWART) mapping for Hebrew descriptions
export const SAP_MOVEMENT_MAP: Record<string, string> = {
  '1|01': 'קבלה רגילה',
  '1|02': 'לידה',
  '1|03': 'ילוד',
  '1|04': 'תאונת דרכים',
  '1|05': 'תאונת עבודה',
  '1|09': 'חשוד לאחר DRG',
  '1|10': 'לאחר DRG',
  '1|11': 'העברה ממרפאות',
  '1|50': 'פעולות איבה',
  
  '2|10': 'הביתה',
  '2|15': 'טיפול ביתי',
  '2|16': 'אשפוז בית',
  '2|20': 'למוסד אחר',
  '2|30': 'סירב לקבל טיפול',
  '2|40': 'פטירה',
  '2|50': 'שוחרר ללא מנהלה',
  '2|51': 'עזב עם הגיליון',
  '2|52': 'עזב על דעת עצמו',
  '2|60': 'אחר',
  '2|70': 'התקבל מת',
  '2|95': 'הסבה - ביטול אשפוז',
  '2|96': 'הסבה - העברה למח\'',
  '2|97': 'הסבה - כללי',
  '2|98': 'הסבה - התקבל מת',
  '2|C4': 'בטול אשפוז/מיון',
  '2|C6': 'נפגע אר״ן בבי״ח',
  
  '3|10': 'מעבר חדר',
  '3|20': 'מעבר מיטה',
  '3|30': 'מעבר מחלקה - רפוא',
  '3|40': 'מעבר מחלקה - תפוס',
  '3|50': 'בקשת המשפחה',
  '3|60': 'ממיון לאשפוז',
  
  '4|01': 'ביקור במרפאה',
  '4|02': 'ב. חוזר במרפאה',
  '4|04': 'תאונת דרכים',
  '4|05': 'תאונת עבודה',
  '4|06': 'אשפוז יום',
  '4|07': 'אחסון גויה',
  '4|12': 'ב. ר. לאחר אשפוז',
  '4|13': 'ב. ר. לאחר מיון',
  '4|14': 'טרום ניתוח',
  '4|15': 'לאחר אשפוז יום',
  '4|16': 'לפני אשפוז',
  '4|17': 'ללא נוכחות',
  '4|18': 'לפני ניתוח תל״ש',
  '4|19': 'לאחר ניתוח תל״ש',
  '4|20': 'טלמדיסין',
  '4|24': 'ב. ר. טלמד. ל. מיון',
  '4|25': 'ב. ר. טלמד. ל. אשפו',
  '4|26': 'א. יום טלמדיסין',
  '4|50': 'פעולות איבה',
  '4|99': 'במהלך אשפ. - הסבה',
  '4|EO': 'מקרה חירום אמבולטורי',
  '4|OP': 'ניתוח',
  
  '6|01': 'בקשת המטופל',
  '6|02': 'טיפול / בדיקה',
  '6|03': 'ללא רשות',
  '6|04': 'סוף שבוע / חג',
  '6|05': 'אירוע משפחתי',
  '6|06': 'הלך לאיבוד',
  '6|07': 'אחר',
  '6|08': 'חופשה ברה״נ',
  
  '7|01': 'חזרה מתוכננת',
  '7|02': 'שינוי מצב רפואי',
  '7|03': 'חזר דרך מיון',
  '7|04': 'חזר על פי דרישה',
  '7|05': 'לא אותר - שוחרר',
  '7|06': 'מסרב לחזור - שוחרר',
  '7|07': 'נפטר',
  '7|08': 'אחר',
};

// Patient type / billing classification codes
export const PATIENT_TYPE_MAP: Record<string, string> = {
  '1': 'אזרח',
  '2': 'צה״ל',
  '4': 'תושב חוץ',
  '5': 'שטחים',
  '7': 'עובד זר',
  '8': 'ילדי עובד זר',
  '9': 'תיירות מרפא',
  '10': 'או״ם',
  '11': 'משהב״ט',
  '12': 'דייר רחוב',
  '13': 'עובד זר עם אשרה',
  '14': 'שבוי',
  '15': 'תושב ארעי',
  '16': 'פליט',
  '17': 'שוטר',
  '99': 'אחר',
};

// Case type codes
export const CASE_TYPE_MAP: Record<string, string> = {
  '1': 'אשפוז',
  '2': 'אמבולטורי',
  '3': 'מיון',
};

// Triage status levels
export const TRIAGE_STATUSES = {
  RED: 'RED',
  YELLOW: 'YELLOW',
  GREEN: 'GREEN',
  BLACK: 'BLACK',
} as const;

// Ziv Medical Center brand colors
export const ZIV_COLORS = {
  PRIMARY_BLUE: '#1E4E9D',        // Primary Hospital Blue
  VIBRANT_ORANGE: '#F15A24',      // Vibrant Clinical Orange
  TRIAGE_RED: '#DC2626',          // Critical Red
  TRIAGE_YELLOW: '#D97706',       // Moderate Yellow
  TRIAGE_GREEN: '#16A34A',        // Mild Green
  TRIAGE_BLACK: '#1F2937',        // Death / Expectant
  LIGHT_BG: '#F8FAFC',            // Light Clinical Slate
  BORDER_COLOR: '#E2E8F0',        // Crisp Border
  TEXT_PRIMARY: '#0F172A',        // High-contrast Typography
} as const;

// SignalR hub configuration
export const SIGNALR_CONFIG = {
  HUB_URL: '/hubs/aran',
  METHODS: {
    JOIN_INCIDENT: 'JoinIncidentGroup',
    LEAVE_INCIDENT: 'LeaveIncidentGroup',
    BROADCAST_UPDATE: 'BroadcastCasualtyUpdate',
    BROADCAST_KPI: 'BroadcastKpiUpdate',
    BROADCAST_PATIENT: 'BroadcastPatientDetail',
  },
  LISTENERS: {
    RECEIVE_UPDATES: 'ReceiveAranUpdates',
    RECEIVE_KPI: 'ReceiveKpiUpdate',
    RECEIVE_PATIENT: 'ReceivePatientDetail',
    RECEIVE_EVENT: 'ReceiveSystemEvent',
    GROUP_JOINED: 'NotifyGroupJoined',
    GROUP_LEFT: 'NotifyGroupLeft',
  },
} as const;
