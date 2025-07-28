// shared.js - Lógica y datos compartidos entre el chat y el panel

// --- CONSTANTES DE CONFIGURACIÓN ---
const POINTS_PER_MESSAGE = 1;
const RANKS = [
    { name: 'Novato', points: 0 }, { name: 'RC1', points: 50 }, { name: 'RC2', points: 150 },
    { name: 'RC3', points: 300 }, { name: 'RC4', points: 500 }, { name: 'RC5', points: 750 },
    { name: 'RC6', points: 1000 }, { name: 'RC7', points: 1500 }, { name: 'G1', points: 2500 },
    { name: 'G2', points: 4000 }, { name: 'G3', points: 6000 }
].sort((a, b) => b.points - a.points);
// La constante RANK_FOR_VOICE ha sido eliminada.

// --- VARIABLES GLOBALES DE ESTADO ---
let usersData = {};
let manualVoiceUsers = new Set();
let selectedVoice = '';
let voices = [];
let volume = 30;
let voiceEnabled = false;
let rankForVoice = 'RC7'; // <-- NUEVO: Variable para el rango, con 'RC7' como valor por defecto.

// --- COMUNICACIÓN ENTRE PESTAÑAS ---
const channel = new BroadcastChannel('utility_streamer_state');

// --- FUNCIONES DE GESTIÓN DE ESTADO (localStorage) ---
function saveState() {
    localStorage.setItem('usersData', JSON.stringify(usersData));
    localStorage.setItem('manualVoiceUsers', JSON.stringify([...manualVoiceUsers]));
    localStorage.setItem('selectedVoice', selectedVoice);
    localStorage.setItem('volume', volume);
    localStorage.setItem('voiceEnabled', voiceEnabled);
    localStorage.setItem('rankForVoice', rankForVoice); // <-- NUEVO: Guardar el rango seleccionado.
    channel.postMessage({ type: 'STATE_UPDATED' });
}

function loadState() {
    try {
        usersData = JSON.parse(localStorage.getItem('usersData')) || {};
        manualVoiceUsers = new Set(JSON.parse(localStorage.getItem('manualVoiceUsers')) || []);
        selectedVoice = localStorage.getItem('selectedVoice') || '';
        volume = parseInt(localStorage.getItem('volume')) || 30;
        voiceEnabled = localStorage.getItem('voiceEnabled') === 'true';
        rankForVoice = localStorage.getItem('rankForVoice') || 'RC7'; // <-- NUEVO: Cargar el rango seleccionado.
    } catch (e) {
        console.error("Error al cargar el estado desde localStorage:", e);
        usersData = {};
        manualVoiceUsers = new Set();
    }
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
    // MODIFICADO: Ahora usa la variable dinámica 'rankForVoice'
    const requiredRankIndex = RANKS.findIndex(r => r.name === rankForVoice); 
    
    if (userRankIndex === -1 || requiredRankIndex === -1) return false;
    
    return userRankIndex <= requiredRankIndex;
}
