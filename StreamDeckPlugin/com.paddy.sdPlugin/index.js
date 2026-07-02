const streamDeck = require("@elgato/streamdeck").default;
const net = require("net");

const logger = streamDeck.logger.createScope("PaDDY");

let client = null;
let isRecording = false;

function connectToPaDDY() {
    if (client) return;
    client = new net.Socket();

    client.on("connect", () => {
        logger.info("Connected to PaDDY");
        // Request initial state or wait for update
    });

    client.on("data", (data) => {
        const lines = data.toString().split("\n");
        for (const line of lines) {
            if (!line.trim()) continue;
            try {
                const state = JSON.parse(line);
                if (state.type === "padsList") {
                    streamDeck.ui.sendToPropertyInspector({ type: "padsList", pads: state.pads }).catch(e => logger.error("Failed to send to PI", e));
                } else if (state.isRecording !== undefined) {
                    isRecording = state.isRecording;
                }
                
                // Update record action states if possible, but the API doesn't expose a global way to find instances.
                // We'll just rely on onWillAppear for new instances. 
                // A complete plugin would track instances of actions.
            } catch (e) {
                logger.error("Error parsing IPC data", e);
            }
        }
    });

    client.on("close", () => {
        logger.info("Disconnected from PaDDY");
        client = null;
        setTimeout(connectToPaDDY, 2000);
    });

    client.on("error", (err) => {
        logger.error("IPC Error: " + err.message);
        client.destroy();
    });

    client.connect(12900, "127.0.0.1");
}

function sendCommand(cmd, args = {}, action) {
    if (client && !client.destroyed) {
        client.write(JSON.stringify({ command: cmd, ...args }) + "\n");
        if (action) action.showOk();
    } else {
        logger.warn("Not connected to PaDDY");
        if (action) action.showAlert();
    }
}

const activeActions = new Map();

// Global action handlers
streamDeck.actions.onKeyDown((ev) => {
    if (ev.action.manifestId === "com.paddy.record") {
        sendCommand("ToggleRecord", {}, ev.action);
    } else if (ev.action.manifestId === "com.paddy.buffer") {
        sendCommand("TriggerKeyBuffer", {}, ev.action);
    } else if (ev.action.manifestId === "com.paddy.play") {
        const settings = ev.payload.settings || {};
        if (settings.padId) {
            sendCommand("PlayPad", { padId: settings.padId }, ev.action);
        } else {
            ev.action.showAlert();
        }
    }
});

streamDeck.actions.onWillAppear((ev) => {
    activeActions.set(ev.action.id, ev.action);
    if (ev.action.manifestId === "com.paddy.record") {
        ev.action.setState(isRecording ? 1 : 0);
    }
});

streamDeck.actions.onWillDisappear((ev) => {
    activeActions.delete(ev.action.id);
});

streamDeck.ui.onSendToPlugin((ev) => {
    if (ev.payload && ev.payload.command === "getPads") {
        sendCommand("GetPads", {});
    }
});

streamDeck.system.onApplicationDidLaunch((ev) => {
    if (ev.payload.application === "PaDDY.exe" || ev.payload.application === "NoIDSoftwork.Core.exe") {
        if (!client) connectToPaDDY();
    }
});

connectToPaDDY();
streamDeck.connect();
