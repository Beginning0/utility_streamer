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

// --- Auto-limpieza de usuarios inactivos ---
let autoCleanupEnabled = false;
let cleanupInactivityDays = 3;

// --- Umbral de migración: usuarios con más puntos van a IndexedDB ---
const MIGRATE_POINTS_THRESHOLD = 500;

// --- COMUNICACIÓN ENTRE PESTAÑAS ---
const channel = new BroadcastChannel('utility_streamer_state');

// --- IndexedDB respaldo (usuarios antiguos / grandes) ---
const DB_NAME = 'utility_streamer_db';
const DB_VERSION = 1;

function openDB() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = () => {
            const db = req.result;
            if (!db.objectStoreNames.contains('users')) db.createObjectStore('users', { keyPath: 'username' });
            if (!db.objectStoreNames.contains('config')) db.createObjectStore('config', { keyPath: 'key' });
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

// Guardar todo en IndexedDB (async, sin bloquear UI)
async function saveToDB() {
    try {
        const db = await openDB();
        const tx = db.transaction(['users', 'config'], 'readwrite');
        tx.objectStore('users').clear();

        Object.keys(usersData).forEach(key => {
            tx.objectStore('users').put({ username: key, ...usersData[key] });
        });
        manualVoiceUsers.forEach(u => tx.objectStore('users').put({ username: u, _manual: true }));
        ttsBlockedUsers.forEach(u => tx.objectStore('users').put({ username: u, _blocked: true }));

        const config = {
            selectedVoice, volume, voiceEnabled, rankForVoice, emoteNoiseThreshold,
            emoteNoiseReductionPercent, announcementBarEnabled, announcementBarMinChars,
            announcementBarDurationSec, clipCommandEnabled, clipActionName,
            clipCommandCooldownSec, clipOnlyMods, autoCleanupEnabled, cleanupInactivityDays
        };
        Object.keys(config).forEach(key => tx.objectStore('config').put({ key, value: config[key] }));
    } catch (e) { console.warn('IndexedDB save falló:', e); }
}

// Cargar desde IndexedDB — fallback si localStorage está vacío/corrupto
async function loadFromDB() {
    try {
        const db = await openDB();
        const allUsers = await new Promise(r => { const req = db.transaction('users').objectStore('users').getAll(); req.onsuccess = () => r(req.result); });
        const allConfig = await new Promise(r => { const req = db.transaction('config').objectStore('config').getAll(); req.onsuccess = () => r(req.result); });

        allUsers.filter(u => !u._manual && !u._blocked).forEach(u => {
            usersData[u.username] = { points: u.points, platform: u.platform, rank: u.rank, lastActive: u.lastActive };
        });
        allUsers.filter(u => u._manual).forEach(u => manualVoiceUsers.add(u.username));
        allUsers.filter(u => u._blocked).forEach(u => ttsBlockedUsers.add(u.username));

        allConfig.forEach(c => {
            const key = c.key;
            if (key === 'selectedVoice') selectedVoice = c.value;
            else if (key === 'volume') volume = c.value;
            else if (key === 'voiceEnabled') voiceEnabled = c.value;
            else if (key === 'rankForVoice') rankForVoice = c.value;
            else if (key === 'emoteNoiseThreshold') emoteNoiseThreshold = c.value;
            else if (key === 'emoteNoiseReductionPercent') emoteNoiseReductionPercent = c.value;
            else if (key === 'announcementBarEnabled') announcementBarEnabled = c.value;
            else if (key === 'announcementBarMinChars') announcementBarMinChars = c.value;
            else if (key === 'announcementBarDurationSec') announcementBarDurationSec = c.value;
            else if (key === 'clipCommandEnabled') clipCommandEnabled = c.value;
            else if (key === 'clipActionName') clipActionName = c.value;
            else if (key === 'clipCommandCooldownSec') clipCommandCooldownSec = c.value;
            else if (key === 'clipOnlyMods') clipOnlyMods = c.value;
            else if (key === 'autoCleanupEnabled') autoCleanupEnabled = c.value;
            else if (key === 'cleanupInactivityDays') cleanupInactivityDays = c.value;
        });
    } catch (e) { console.warn('IndexedDB no disponible:', e); }
}

// --- LOCALSTORAGE primario + MIGRACIÓN ---
function saveState() {
    // Decidir qué usuarios van a localStorage vs IndexedDB
    const lsUsers = {};
    Object.keys(usersData).forEach(key => {
        if (usersData[key].points < MIGRATE_POINTS_THRESHOLD) {
            lsUsers[key] = usersData[key];
        }
    });

    localStorage.setItem('usersData', JSON.stringify(lsUsers));

    manualVoiceUsers.forEach(u => localStorage.setItem('manualVoiceUser:' + u, '1'));
    ttsBlockedUsers.forEach(u => localStorage.setItem('ttsBlockedUser:' + u, '1'));
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
    localStorage.setItem('autoCleanupEnabled', autoCleanupEnabled);
    localStorage.setItem('cleanupInactivityDays', cleanupInactivityDays);

    channel.postMessage({ type: 'STATE_UPDATED' });

    // Respaldo async en IndexedDB (siempre)
    saveToDB();
}

function loadState() {
    try {
        usersData = JSON.parse(localStorage.getItem('usersData')) || {};

        manualVoiceUsers = new Set();
        ttsBlockedUsers = new Set();
        for (let i = 0; i < localStorage.length; i++) {
            const key = localStorage.key(i);
            if (key && key.startsWith('manualVoiceUser:')) manualVoiceUsers.add(key.split(':')[1]);
            if (key && key.startsWith('ttsBlockedUser:')) ttsBlockedUsers.add(key.split(':')[1]);
        }

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
        autoCleanupEnabled = localStorage.getItem('autoCleanupEnabled') === 'true';
        cleanupInactivityDays = parseInt(localStorage.getItem('cleanupInactivityDays')) || 3;
    } catch (e) {
        console.error('localStorage corrupto, usando IndexedDB:', e);
        usersData = {}; manualVoiceUsers = new Set(); ttsBlockedUsers = new Set();
        loadFromDB();
    }
}

// --- MIGRACIÓN: cuando un usuario crece, pasar a IndexedDB ---
function maybeMigrateUser(username) {
    if (!usersData[username]) return;

    const inLS = localStorage.getItem('usersData');
    let lsUsers = {};
    try { lsUsers = JSON.parse(inLS) || {}; } catch(e) { return; }

    // Si el usuario ya está en localStorage y alcanzó el umbral, migrarlo
    if (lsUsers[username] && usersData[username].points >= MIGRATE_POINTS_THRESHOLD) {
        const toMigrate = { ...usersData[username] };
        Object.keys(usersData).forEach(key => {
            if (key !== username && usersData[key].points < MIGRATE_POINTS_THRESHOLD) {
                lsUsers[key] = usersData[key];
            }
        });
        localStorage.setItem('usersData', JSON.stringify(lsUsers));
        console.log(`Migrado ${username} a IndexedDB (${toMigrate.points} pts)`);
    }

    // Si localStorage está vacío pero tenemos usuarios en memoria, guardarlos
    if (!inLS || inLS === 'null') {
        saveToDB();
    }
}

// --- LÓGICA DE USUARIOS, PUNTOS Y RANGOS ---
function getUserData(displayName, platform) {
    const lowerUser = displayName.toLowerCase();
    if (!usersData[lowerUser]) {
        usersData[lowerUser] = { points: 0, platform: platform, rank: 'Novato', lastActive: Date.now() };
    } else {
        if (platform && !usersData[lowerUser].platform) usersData[lowerUser].platform = platform;
        usersData[lowerUser].displayName = displayName;
        usersData[lowerUser].lastActive = Date.now();
    }
    return usersData[lowerUser];
}

function addPointsForChat(displayName, platform) {
    const lowerUser = displayName.toLowerCase();
    if (!usersData[lowerUser]) {
        usersData[lowerUser] = { points: 0, platform: platform, rank: 'Novato', lastActive: Date.now() };
    }
    usersData[lowerUser].points += POINTS_PER_MESSAGE;

    // Verificar migración después de sumar puntos
    maybeMigrateUser(lowerUser);

    updateRankForChat(displayName);
}

function getUserRank(username) {
    const data = usersData[username.toLowerCase()] || { points: 0 };
    return RANKS.find(r => data.points >= r.points) || RANKS[RANKS.length - 1];
}

function hasVoicePermission(displayName, platform) {
    const lowerUser = displayName.toLowerCase();

    // No bloquear mensajes del sistema (Bot)
    if (displayName === 'Bot' || platform === 'Sistema') return false;

    if (ttsBlockedUsers.has(lowerUser)) return false;
    if (manualVoiceUsers.has(lowerUser)) return true;  // ← ANTES del rango

    const data = usersData[lowerUser] || { points: 0, rank: 'Novato' };
    const userRankIndex = RANKS.findIndex(r => r.name === data.rank);
    const requiredRankIndex = RANKS.findIndex(r => r.name === rankForVoice);

    if (userRankIndex === -1 || requiredRankIndex === -1) return false;

    return userRankIndex <= requiredRankIndex;
}

// --- Limpieza de usuarios inactivos ---
function cleanupInactiveUsers() {
    if (!autoCleanupEnabled) return;
    const cutoff = Date.now() - (cleanupInactivityDays * 86400000);
    let removed = 0;

    Object.keys(usersData).forEach(lowerName => {
        const user = usersData[lowerName];
        if (user.lastActive && user.lastActive < cutoff) {
            delete usersData[lowerName];
            manualVoiceUsers.delete(lowerName);
            ttsBlockedUsers.delete(lowerName);
            removed++;
        }
    });

    if (removed > 0) saveState();
}

// --- Validación de rangos ---
emoteNoiseThreshold = Math.max(1, Math.min(30, emoteNoiseThreshold));
emoteNoiseReductionPercent = Math.max(0, Math.min(90, emoteNoiseReductionPercent));
announcementBarMinChars = Math.max(5, Math.min(300, announcementBarMinChars));
announcementBarDurationSec = Math.max(5, Math.min(45, announcementBarDurationSec));
clipCommandCooldownSec = Math.max(5, Math.min(300, clipCommandCooldownSec));
if (!clipActionName) clipActionName = 'Crear Clip';
