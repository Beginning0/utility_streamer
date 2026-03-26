// shared.js - Lógica y datos compartidos entre el chat y el panel

// --- CONSTANTES DE CONFIGURACIÓN ---
const POINTS_PER_MESSAGE = 1;
const RANKS = [
    { name: 'Novato', points: 0 }, { name: 'RC1', points: 10 }, { name: 'RC2', points: 150 },
    { name: 'RC3', points: 300 }, { name: 'RC4', points: 500 }, { name: 'RC5', points: 750 },
    { name: 'RC6', points: 1000 }, { name: 'RC7', points: 1500 }, { name: 'G1', points: 2500 },
    { name: 'G2', points: 4000 }, { name: 'G3', points: 6000 }
].sort((a, b) => b.points - a.points);

// --- VARIABLES GLOBALES DE ESTADO ---
let usersData = {};
let manualVoiceUsers = new Set();
let ttsBlockedUsers = new Set();
let selectedVoice = '';
let voices = [];
let volume = 30;
let voiceEnabled = false;
let rankForVoice = 'RC7';
let emoteNoiseThreshold = 4;
let emoteNoiseReductionPercent = 55;
let announcementBarEnabled = false;
let announcementBarMinChars = 40;
let announcementBarDurationSec = 10;
let clipCommandEnabled = true;
let clipActionName = 'Crear Clip';
let clipCommandCooldownSec = 45;
let clipOnlyMods = true;

// --- COMUNICACIÓN ENTRE PESTAÑAS ---
const channel = new BroadcastChannel('utility_streamer_state');

// --- FUNCIONES DE GESTIÓN DE ESTADO (localStorage) ---
function saveState() {
    localStorage.setItem('usersData', JSON.stringify(usersData));
    localStorage.setItem('manualVoiceUsers', JSON.stringify([...manualVoiceUsers]));
    localStorage.setItem('ttsBlockedUsers', JSON.stringify([...ttsBlockedUsers]));
    localStorage.setItem('selectedVoice', selectedVoice);
    localStorage.setItem('volume', volume);
    localStorage.setItem('voiceEnabled', voiceEnabled);
    localStorage.setItem('rankForVoice', rankForVoice);
    localStorage.setItem('emoteNoiseThreshold', emoteNoiseThreshold);
    localStorage.setItem('emoteNoiseReductionPercent', emoteNoiseReductionPercent);
    localStorage.setItem('announcementBarEnabled', announcementBarEnabled);
    localStorage.setItem('announcementBarMinChars', announcementBarMinChars);
    localStorage.setItem('announcementBarDurationSec', announcementBarDurationSec);
    localStorage.setItem('clipCommandEnabled', clipCommandEnabled);
    localStorage.setItem('clipActionName', clipActionName);
    localStorage.setItem('clipCommandCooldownSec', clipCommandCooldownSec);
    localStorage.setItem('clipOnlyMods', clipOnlyMods);
    channel.postMessage({ type: 'STATE_UPDATED' });
}

function loadState() {
    try {
        usersData = JSON.parse(localStorage.getItem('usersData')) || {};
        manualVoiceUsers = new Set(JSON.parse(localStorage.getItem('manualVoiceUsers')) || []);
        ttsBlockedUsers = new Set(JSON.parse(localStorage.getItem('ttsBlockedUsers')) || []);
        selectedVoice = localStorage.getItem('selectedVoice') || '';
        volume = parseInt(localStorage.getItem('volume')) || 30;
        voiceEnabled = localStorage.getItem('voiceEnabled') === 'true';
        rankForVoice = localStorage.getItem('rankForVoice') || 'RC7';
        emoteNoiseThreshold = parseInt(localStorage.getItem('emoteNoiseThreshold')) || 4;
        emoteNoiseReductionPercent = parseInt(localStorage.getItem('emoteNoiseReductionPercent')) || 55;
        announcementBarEnabled = localStorage.getItem('announcementBarEnabled') === 'true';
        announcementBarMinChars = parseInt(localStorage.getItem('announcementBarMinChars')) || 40;
        announcementBarDurationSec = parseInt(localStorage.getItem('announcementBarDurationSec')) || 10;
        clipCommandEnabled = localStorage.getItem('clipCommandEnabled') !== 'false';
        clipActionName = (localStorage.getItem('clipActionName') || 'Crear Clip').trim();
        clipCommandCooldownSec = parseInt(localStorage.getItem('clipCommandCooldownSec')) || 45;
        clipOnlyMods = localStorage.getItem('clipOnlyMods') !== 'false';

        emoteNoiseThreshold = Math.max(1, Math.min(30, emoteNoiseThreshold));
        emoteNoiseReductionPercent = Math.max(0, Math.min(90, emoteNoiseReductionPercent));
        announcementBarMinChars = Math.max(5, Math.min(300, announcementBarMinChars));
        announcementBarDurationSec = Math.max(5, Math.min(45, announcementBarDurationSec));
        clipCommandCooldownSec = Math.max(5, Math.min(300, clipCommandCooldownSec));
        if (!clipActionName) clipActionName = 'Crear Clip';
    } catch (e) {
        console.error("Error al cargar el estado desde localStorage:", e);
        usersData = {}; manualVoiceUsers = new Set(); ttsBlockedUsers = new Set();
    }
}

// --- LÓGICA DE USUARIOS, PUNTOS Y RANGOS ---
function getUserData(displayName, platform) {
    const lowerUser = displayName.toLowerCase();
    if (!usersData[lowerUser]) {
        // Si el usuario es nuevo, lo creamos con toda la información
        usersData[lowerUser] = { points: 0, rank: 'Novato', displayName: displayName, platform: platform };
    } else {
        // Si el usuario ya existe, nos aseguramos de que su plataforma esté registrada
        if (platform && !usersData[lowerUser].platform) {
            usersData[lowerUser].platform = platform;
        }
        // Siempre actualizamos el displayName por si cambia mayúsculas/minúsculas
        usersData[lowerUser].displayName = displayName;
    }
    return usersData[lowerUser];
}

function hasVoicePermission(displayName, platform) {
    const lowerUser = displayName.toLowerCase();
    
    // CORRECCIÓN: No bloquear mensajes del sistema (Bot)
    if (displayName === 'Bot' || platform === 'Sistema') {
        return false; // El bot del sistema no debe usar TTS
    }
    
    if (ttsBlockedUsers.has(lowerUser)) return false;
    if (manualVoiceUsers.has(lowerUser)) return true;
    
    const userData = getUserData(displayName, platform);
    const userRankIndex = RANKS.findIndex(r => r.name === userData.rank);
    const requiredRankIndex = RANKS.findIndex(r => r.name === rankForVoice); 
    
    if (userRankIndex === -1 || requiredRankIndex === -1) return false;
    
    return userRankIndex <= requiredRankIndex;
}
