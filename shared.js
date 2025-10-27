// =================================================================
// ARCHIVO: shared.js
// Lógica y datos compartidos - VERSIÓN FINAL CORREGIDA
// =================================================================

const POINTS_PER_MESSAGE = 1;
let pointsPerGiftedMembership = 500;
let tickerSpeed = 80;

let RANKS = [
    { name: 'Novato', points: 0 }, { name: 'RC1', points: 50 }, { name: 'RC2', points: 150 },
    { name: 'RC3', points: 300 }, { name: 'RC4', points: 500 }, { name: 'RC5', points: 750 },
    { name: 'RC6', points: 1000 }, { name: 'RC7', points: 1500 }, { name: 'G1', points: 2500 },
    { name: 'G2', points: 4000 }, { name: 'G3', points: 6000 }
];

let usersData = {}, manualVoiceUsers = new Set(), ttsBlockedUsers = new Set();
let selectedVoice = '', voices = [], volume = 30, voiceEnabled = false, rankForVoice = 'RC7';
let chatDisplayMode = 'normal', bannerPosition = 'bottom';

const channel = new BroadcastChannel('utility_streamer_state');

function saveState() {
    try {
        const state = {
            usersData, manualVoiceUsers: Array.from(manualVoiceUsers), ttsBlockedUsers: Array.from(ttsBlockedUsers),
            selectedVoice, volume, voiceEnabled, rankForVoice, ranks: RANKS,
            chatDisplayMode, bannerPosition, pointsPerGiftedMembership,
            tickerSpeed
        };
        localStorage.setItem('utilityStreamerState', JSON.stringify(state));
        channel.postMessage({ type: 'STATE_UPDATED' });
    } catch (error) { console.error("Error al guardar el estado:", error); }
}

function loadState() {
    const savedState = localStorage.getItem('utilityStreamerState');
    try {
        if (savedState) {
            const state = JSON.parse(savedState);
            usersData = state.usersData || {};
            manualVoiceUsers = new Set(state.manualVoiceUsers || []);
            ttsBlockedUsers = new Set(state.ttsBlockedUsers || []);
            selectedVoice = state.selectedVoice || '';
            volume = state.volume !== undefined ? state.volume : 30;
            voiceEnabled = state.voiceEnabled || false;
            rankForVoice = state.rankForVoice || 'RC7';
            RANKS = state.ranks || RANKS;
            chatDisplayMode = state.chatDisplayMode || 'normal';
            bannerPosition = state.bannerPosition || 'bottom';
            pointsPerGiftedMembership = state.pointsPerGiftedMembership !== undefined ? state.pointsPerGiftedMembership : 500;
            tickerSpeed = state.tickerSpeed !== undefined ? state.tickerSpeed : 80;
        }
    } catch (e) {
        // --- [INICIO DE LA CORRECCIÓN] ---
        // Si hay un error al leer los datos (porque son de una versión antigua),
        // borramos los datos corruptos y empezamos de cero.
        console.error("Error al cargar el estado. Probablemente datos de una versión anterior. Reiniciando estado.", e);
        localStorage.removeItem('utilityStreamerState'); // Eliminamos los datos malos
        usersData = {};
        manualVoiceUsers = new Set();
        ttsBlockedUsers = new Set();
        // Guardamos un estado limpio para no volver a fallar
        saveState();
        // --- [FIN DE LA CORRECCIÓN] ---
    }
}

function getUserData(displayName, platform) {
    const key = `${displayName.toLowerCase()}_${platform}`;
    if (!usersData[key]) {
        usersData[key] = { displayName, platform, points: 0, rank: 'Novato' };
    }
    // Aseguramos que el nombre de usuario siempre esté actualizado (por si cambian mayúsculas/minúsculas)
    usersData[key].displayName = displayName;
    if (!usersData[key].platform) {
        usersData[key].platform = platform;
    }
    return usersData[key];
}

function hasVoicePermission(displayName, platform) {
    const userKey = `${displayName.toLowerCase()}_${platform}`;
    if (ttsBlockedUsers.has(userKey)) return false;
    if (manualVoiceUsers.has(userKey)) return true;
    const userData = getUserData(displayName, platform);
    const requiredRank = RANKS.find(r => r.name === rankForVoice);
    if (!requiredRank) return false;
    return userData.points >= requiredRank.points;
}

loadState();
