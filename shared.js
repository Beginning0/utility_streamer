// shared.js - Lógica y datos compartidos entre el chat y el panel

// --- CONFIGURACIÓN ---
const POINTS_PER_MESSAGE = 1;
const RANKS = [
    { name: 'Novato', points: 0 }, { name: 'RC1', points: 50 }, { name: 'RC2', points: 150 },
    { name: 'RC3', points: 300 }, { name: 'RC4', points: 500 }, { name: 'RC5', points: 750 },
    { name: 'RC6', points: 1000 }, { name: 'RC7', points: 1500 }, { name: 'G1', points: 2500 },
    { name: 'G2', points: 4000 }, { name: 'G3', points: 6000 }
].sort((a, b) => b.points - a.points);
const RANK_FOR_VOICE = 'RC7';

// --- ESTADO GLOBAL ---
// Estas variables serán pobladas por la función loadState()
let usersData = {};
let manualVoiceUsers = new Set();
let selectedVoice = '';
let voices = [];
let volume = 30;
let voiceEnabled = false;

// --- CANAL DE COMUNICACIÓN ENTRE PESTAÑAS ---
const channel = new BroadcastChannel('streamer_tool_channel');

// --- FUNCIONES DE GESTIÓN DE ESTADO (localStorage) ---
function saveState() {
    localStorage.setItem('usersData', JSON.stringify(usersData));
    localStorage.setItem('manualVoiceUsers', JSON.stringify([...manualVoiceUsers]));
    localStorage.setItem('selectedVoice', selectedVoice);
    localStorage.setItem('volume', volume);
    localStorage.setItem('voiceEnabled', voiceEnabled);
    // Notificar a otras pestañas que el estado ha cambiado
    channel.postMessage({ type: 'STATE_UPDATED' });
}

function loadState() {
    usersData = JSON.parse(localStorage.getItem('usersData')) || {};
    manualVoiceUsers = new Set(JSON.parse(localStorage.getItem('manualVoiceUsers')) || []);
    selectedVoice = localStorage.getItem('selectedVoice') || '';
    volume = parseInt(localStorage.getItem('volume')) || 30;
    voiceEnabled = localStorage.getItem('voiceEnabled') === 'true';
}

// --- LÓGICA DE USUARIOS, PUNTOS Y RANGOS ---
function getUserData(displayName) {
    const lowerUser = displayName.toLowerCase();
    if (!usersData[lowerUser]) {
        usersData[lowerUser] = { points: 0, rank: 'Novato', displayName: displayName };
    }
    usersData[lowerUser].displayName = displayName;
    return usersData[lowerUser];
}

function hasVoicePermission(displayName) {
    const lowerUser = displayName.toLowerCase();
    if (manualVoiceUsers.has(lowerUser)) return true;
    const userData = getUserData(displayName);
    const userRankIndex = RANKS.findIndex(r => r.name === userData.rank);
    const requiredRankIndex = RANKS.findIndex(r => r.name === RANK_FOR_VOICE);
    if (userRankIndex === -1 || requiredRankIndex === -1) return false;
    return userRankIndex <= requiredRankIndex;
}