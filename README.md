# Utility Streamer

<div align="center">

![YouTube](https://img.shields.io/badge/YouTube-FF0033?style=for-the-badge&logo=youtube&logoColor=white)
![Twitch](https://img.shields.io/badge/Twitch-9146FF?style=for-the-badge&logo=twitch&logoColor=white)

**El overlay de chat todo-en-uno para tus streams en directo.** Conecta **YouTube** y **Twitch** a un solo panel para OBS: chat, voz (TTS), puntos, rangos y comandos personalizados.

[📖 Ver guía de instalación](https://beginning0.github.io/utility_streamer/index.html) · [💬 Chat en vivo](#) · [🎮 Twitch: shado_terro](https://www.twitch.tv/shado_terro)

</div>

---

## ¿Qué es?

`Utility Streamer` es una herramienta web que se integra con **Streamer.bot** y muestra el chat de YouTube + Twitch como un **overlay para OBS**, permitiendo además **escuchar los mensajes en voz alta** (Text-to-Speech). Está diseñada para streamers que quieren más interacción con su audiencia sin depender de plugins costosos.

> Funciona tanto para **Twitch** como para **YouTube Live** — esta última es una ventaja, ya que YouTube no trae chat interactivo de forma nativa como Twitch.

## ✨ Funcionalidades

- 💬 **Chat unificado** — YouTube y Twitch en una sola ventana.
- 🔊 **Voz (TTS)** — escucha los mensajes en voz alta con control de volumen, voz y umbral de rango.
- ⭐ **Puntos y rangos** — gana puntos por participar y sube de rango; umbrales totalmente configurables.
- 🎛️ **Comandos personalizados** — crea comandos que disparan *acciones de Streamer.bot* (como `!clip`), con **cooldown propio por comando**.
- 🛡️ **Moderación** — identifica mods/owner y restringe comandos solo a ellos.
- 📣 **Barra de anuncios** — mensajes destacados en pantalla con duración configurable.
- 💾 **Persistencia inteligente** — el estado se guarda en `localStorage` + `IndexedDB` (los usuarios con más puntos migran solos, sin llenar memoria).
- 🧹 **Auto-limpieza** — elimina usuarios inactivos automáticamente.

## 📋 Requisitos

1. Tener **Streamer.bot** instalado y funcionando: [Descárgalo aquí](https://streamer.bot/) (compatible con la versión **1.5.x**).
2. Activar el **WebSocket Server** en `Servers/Clients → WebSocket Server` con:
   - `Host: 127.0.0.1`
   - `Port: 4456`
   - `Endpoint: /`
3. Un navegador web moderno (Chrome/Edge recomendado).

## 🚀 Instalación en OBS

La forma más fácil de empezar es seguir la **Guía de Instalación en OBS**, una página visual paso a paso donde podrás copiar las URLs del overlay y del panel directamente:

👉 **[https://beginning0.github.io/utility_streamer/index.html](https://beginning0.github.io/utility_streamer/index.html)**

En resumen, son 3 pasos:

1. **Configura Streamer.bot** — activa el servidor WebSocket (puerto `4456`).
2. **Añade el Overlay de Chat** — fuente *Navegador* en OBS (ancho `1920` × alto `1080`).
3. **Añade el Panel de Control** — *Paneles de navegador personalizados* en OBS, solo visible para ti.

## 🧑‍💻 Desarrollo

Cada archivo tiene un propósito:

| Archivo | Descripción |
|---------|-------------|
| `chat.html` | El overlay que se conecta a Streamer.bot y muestra el chat + voz. |
| `panel.html` | Panel de administración (comandos, rangos, TTS, moderación). |
| `shared.js` | Estado compartido entre pestañas (usuarios, rangos, comandos) con `localStorage` + `IndexedDB`. |

## 📄 Cambios y historial

Consulta el detalle de todas las mejoras aplicadas en [`CHANGES.md`](./CHANGES.md).

## 🌐 Apoyo en redes

Si te gusta este proyecto y quieres ayudarme a seguir desarrollándolo:

- [Twitch: shado_terro](https://www.twitch.tv/shado_terro)
- [YouTube: ShadoTerro Playing](https://youtube.com/@ShadoTerroPlaying)
- [YouTube: ShadoTerro Hobby](https://youtube.com/@ShadoTerroHobby)

## ⚠️ Exención de responsabilidad

Utility Streamer se proporciona "tal cual" y "según disponibilidad". El uso de esta herramienta es bajo tu propio riesgo. No soy responsable por ningún daño o pérdida que pueda surgir del uso de Utility Streamer.

## 📄 Licencia

Este proyecto está licenciado bajo los términos de la [Licencia MIT](LICENSE).

---

¿Tienes preguntas o sugerencias? Abre un *issue* en el repositorio. ¡Gracias por usar Utility Streamer!
