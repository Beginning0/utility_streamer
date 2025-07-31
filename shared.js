// shared.js - Lógica y datos compartidos entre el chat y el panel

// --- CONSTANTES DE CONFIGURACIÓN ---
const POINTS_PER_MESSAGE = 1;
const RANKS = [
    { name: 'Novato', points: 0 }, { name: 'RC1', points: 50 }, { name: 'RC2', points: 150 },
    { name: 'RC3', points: 300 }, { name: 'RC4', points: 500 }, { name: 'RC5', points: 750 },
    { name: 'RC6', points: 1000 }, { name: 'RC7', points: 1500 }, { name: 'G1', points: 2500 },
    { name: 'G2', points: 4000 }, { name: 'G3', points: 6000 }
].sort((a, b) => b.points - a.points);

// --- VARIABLES GLOBALES DE ESTADO ---
let usersData = {};
let manualVoiceUsers = new Set();
let ttsBlockedUsers = new Set(); // <-- NUEVO: Lista de bloqueo para TTS
let selectedVoice = '';
let voices = [];
let volume = 30;
let voiceEnabled = false;
let rankForVoice = 'RC7';

// --- COMUNICACIÓN ENTRE PESTAÑAS ---
const channel = new BroadcastChannel('utility_streamer_state');

// --- FUNCIONES DE GESTIÓN DE ESTADO (localStorage) ---
function saveState() {
    localStorage.setItem('usersData', JSON.stringify(usersData));
    localStorage.setItem('manualVoiceUsers', JSON.stringify([...manualVoiceUsers]));
    localStorage.setItem('ttsBlockedUsers', JSON.stringify([...ttsBlockedUsers])); // <-- NUEVO
    localStorage.setItem('selectedVoice', selectedVoice);
    localStorage.setItem('volume', volume);
    localStorage.setItem('voiceEnabled', voiceEnabled);
    localStorage.setItem('rankForVoice', rankForVoice);
    channel.postMessage({ type: 'STATE_UPDATED' });
}

function loadState() {
    try {
        usersData = JSON.parse(localStorage.getItem('usersData')) || {};
        manualVoiceUsers = new Set(JSON.parse(localStorage.getItem('manualVoiceUsers')) || []);
        ttsBlockedUsers = new Set(JSON.parse(localStorage.getItem('ttsBlockedUsers')) || []); // <-- NUEVO
        selectedVoice = localStorage.getItem('selectedVoice') || '';
        volume = parseInt(localStorage.getItem('volume')) || 30;
        voiceEnabled = localStorage.getItem('voiceEnabled') === 'true';
        rankForVoice = localStorage.getItem('rankForVoice') || 'RC7';
    } catch (e) {
        console.error("Error al cargar el estado desde localStorage:", e);
        usersData = {}; manualVoiceUsers = new Set(); ttsBlockedUsers = new Set();
    }
}

// --- LÓGICA DE USUARIOS, PUNTOS Y RANGOS ---
function getUserData(displayName, platform) { // <-- MODIFICADO: Acepta la plataforma
    const lowerUser = displayName.toLowerCase();
    if (!usersData[lowerUser]) {
        usersData[lowerUser] = { points: 0, rank: 'Novato', displayName: displayName, platform: platform };
    }
    usersData[lowerUser].displayName = displayName;
    usersData[lowerUser].platform = platform; // <-- NUEVO: Siempre actualiza la plataforma
    return usersData[lowerUser];
}

function hasVoicePermission(displayName) {
    const lowerUser = displayName.toLowerCase();
    if (ttsBlockedUsers.has(lowerUser)) return false; // <-- NUEVO: Si está bloqueado, no tiene permiso.
    if (manualVoiceUsers.has(lowerUser)) return true;
    
    const userData = getUserData(displayName);
    const userRankIndex = RANKS.findIndex(r => r.name === userData.rank);
    const requiredRankIndex = RANKS.findIndex(r => r.name === rankForVoice); 
    
    if (userRankIndex === -1 || requiredRankIndex === -1) return false;
    
    return userRankIndex <= requiredRankIndex;
}
