# name=Mixdeck
# url=https://github.com/Anarchiejs/Mixdeck
# Mixdeck pilote le mixeur de FL Studio par SysEx sur deux ports MIDI virtuels (loopMIDI).
# Options > MIDI : entrée « Mixdeck to FL » (type Mixdeck) et sortie « FL to Mixdeck », même numéro de port.
# Après avoir modifié ce fichier, passer le type à « (generic controller) » puis revenir à « Mixdeck » pour le recharger.
#
# Message : F0 7D 4D 58 <commande> <données 7 bits> F7 (7D = identifiant non commercial, 4D 58 = « MX »).
# Les pistes et volumes tiennent sur deux octets (poids fort, poids faible ; 14 bits). Protocole décrit dans
# src/Mixdeck.FlStudio/FlProtocol.cs ; les deux fichiers évoluent ensemble.

import device
import general
import midi
import mixer
import plugins
import channels
import time

PROTOCOL_VERSION = 3
HEADER = bytes([0xF0, 0x7D, 0x4D, 0x58])
SLOTS = 10

CMD_PING = 0x01
CMD_SELECT = 0x02
CMD_MUTE = 0x03
CMD_VOLUME = 0x04
CMD_SLOT = 0x05
CMD_PRESET = 0x06
CMD_SLOTS_ENABLED = 0x07
CMD_GET_TRACK = 0x08
CMD_GET_COUNT = 0x09
CMD_LIST = 0x0A

REPLY_PONG = 0x41
REPLY_TRACK = 0x48
REPLY_COUNT = 0x49
REPLY_LIST_BEGIN = 0x4A
REPLY_LIST_END = 0x4B
REPLY_SELECTED = 0x4C
REPLY_ERROR = 0x7F

ERR_UNKNOWN = 1
ERR_BAD_TRACK = 2
ERR_BAD_SLOT = 3
ERR_LENGTH = 4

# Pistes suivies par Mixdeck : leur état est renvoyé dès qu'il change dans FL.
watched = set()
# Pistes dont l'état doit partir au prochain OnIdle (FL applique les changements après le retour du script).
pending = set()
# Dernier état envoyé par piste, pour n'envoyer que ce qui change.
last_sent = {}
last_selected = -1
echo_reported = False
# Pistes utilisées lors du dernier envoi de la liste ; None tant que Mixdeck ne l'a pas demandée.
listed_tracks = None
next_list_check = 0.0


def u14(data, i):
    return (data[i] << 7) | data[i + 1]


def b14(value):
    value = max(0, min(16383, int(value)))
    return [value >> 7, value & 0x7F]


def send(command, payload):
    if device.isAssigned():
        device.midiOutSysex(HEADER + bytes([command] + payload + [0xF7]))


def slot_event(track, slot):
    return mixer.getTrackPluginId(track, slot) + midi.REC_Plug_Mute


def slot_is_muted(track, slot):
    # Constaté dans FL 2025 : l'événement REC_Plug_Mute vaut 0 quand le slot est coupé (c'est un « actif »).
    return mixer.getEventValue(slot_event(track, slot), midi.MaxInt, False) == 0


def set_slot_muted(track, slot, muted):
    if slot_is_muted(track, slot) == muted:
        return
    # Même bascule que le script Mackie livré avec FL (appui sur l'encodeur = muet du slot).
    event = slot_event(track, slot)
    value = channels.incEventValue(event, 0, midi.EKRes)
    general.processRECEvent(event, value, midi.REC_Controller)


def ascii7(text, limit):
    return [c & 0x7F for c in str(text).encode("ascii", "replace")[:limit]]


def plugin_name(track, slot):
    # Nom affiché dans le slot du mixeur (renommé par l'utilisateur), sinon nom du plugin.
    for user_name in (True, False):
        try:
            name = plugins.getPluginName(track, slot, user_name)
        except Exception:
            name = ""
        if name:
            return name
    return ""


def track_payload(track):
    valid = 0
    muted = 0
    names = []
    for slot in range(SLOTS):
        if mixer.isTrackPluginValid(track, slot):
            valid |= 1 << slot
            if slot_is_muted(track, slot):
                muted |= 1 << slot
            plugin = ascii7(plugin_name(track, slot), 24)
            names += [len(plugin)] + plugin
    name = mixer.getTrackName(track).encode("ascii", "replace")[:32]
    return b14(track) + b14(round(mixer.getTrackVolume(track) * 16383)) \
        + [int(bool(mixer.isTrackMuted(track))), int(bool(mixer.isTrackSlotsEnabled(track)))] \
        + b14(valid) + b14(muted) + [len(name)] + [c & 0x7F for c in name] + names


def send_track(track, only_if_changed=False):
    payload = track_payload(track)
    if only_if_changed and last_sent.get(track) == payload:
        return
    last_sent[track] = payload
    send(REPLY_TRACK, payload)


def select_only(track):
    # setTrackNumber seul laisse l'ancienne piste sélectionnée dans le mixeur (constaté dans FL 2025) :
    # tout désélectionner, puis rendre la piste courante et sélectionnée.
    mixer.deselectAll()
    mixer.setTrackNumber(track, midi.curfxScrollToMakeVisible)
    if not mixer.isTrackSelected(track):
        mixer.selectTrack(track)


def send_selected():
    global last_selected
    selected = mixer.trackNumber()
    if selected != last_selected:
        last_selected = selected
        send(REPLY_SELECTED, b14(selected))


def used_tracks():
    return [track for track in range(mixer.trackCount()) if is_used(track)]


def send_list(tracks):
    # Liste complète encadrée (début, états, fin) : Mixdeck remplace ses pistes par celles-ci.
    global listed_tracks
    listed_tracks = tracks
    send(REPLY_LIST_BEGIN, [])
    for track in tracks:
        watched.add(track)
        send_track(track)
    send(REPLY_LIST_END, b14(len(tracks)))


def is_used(track):
    # Master, piste renommée ou piste avec au moins un effet : les pistes vides restent cachées.
    if track == 0 or mixer.getTrackName(track) != "Insert " + str(track):
        return True
    return any(mixer.isTrackPluginValid(track, slot) for slot in range(SLOTS))


def error(command, code):
    send(REPLY_ERROR, [command, code])


def handle(command, data):
    global last_selected
    if command == CMD_PING:
        send(REPLY_PONG, b14(general.getVersion()) + [PROTOCOL_VERSION])
        return
    if command == CMD_GET_COUNT:
        send(REPLY_COUNT, b14(mixer.trackCount()))
        return
    if command == CMD_LIST:
        send_list(used_tracks())
        last_selected = -1
        send_selected()
        return
    if len(data) < 2:
        error(command, ERR_LENGTH)
        return
    track = u14(data, 0)
    if track >= mixer.trackCount():
        error(command, ERR_BAD_TRACK)
        return
    args = data[2:]
    watched.add(track)
    if command == CMD_GET_TRACK:
        send_track(track)
        return
    if command == CMD_SELECT:
        select_only(track)
    elif command == CMD_MUTE and len(args) >= 1:
        if bool(mixer.isTrackMuted(track)) != bool(args[0]):
            mixer.muteTrack(track)
    elif command == CMD_VOLUME and len(args) >= 2:
        mixer.setTrackVolume(track, u14(args, 0) / 16383)
    elif command == CMD_SLOTS_ENABLED and len(args) >= 1:
        mixer.enableTrackSlots(track, 1 if args[0] else 0)
    elif command in (CMD_SLOT, CMD_PRESET) and len(args) >= 2:
        slot = args[0]
        if slot >= SLOTS or not mixer.isTrackPluginValid(track, slot):
            error(command, ERR_BAD_SLOT)
            return
        if command == CMD_SLOT:
            set_slot_muted(track, slot, args[1] == 0)
        elif args[1]:
            plugins.nextPreset(track, slot)
        else:
            plugins.prevPreset(track, slot)
    elif command in (CMD_MUTE, CMD_VOLUME, CMD_SLOTS_ENABLED, CMD_SLOT, CMD_PRESET):
        error(command, ERR_LENGTH)
        return
    else:
        error(command, ERR_UNKNOWN)
        return
    # La réponse part toujours, même sans changement : Mixdeck s'en sert d'accusé de réception.
    last_sent.pop(track, None)
    pending.add(track)


def OnInit():
    print("Mixdeck : script chargé (API " + str(general.getVersion()) + ", protocole " + str(PROTOCOL_VERSION)
          + "), sortie liée : " + str(bool(device.isAssigned())))


def OnDeInit():
    watched.clear()
    pending.clear()
    last_sent.clear()


def OnRefresh(flags):
    # Changement fait dans FL (souris, automation, autre contrôleur) : comparer les pistes suivies au prochain OnIdle.
    pending.update(watched)


def OnIdle():
    global next_list_check
    # Nouveau projet, plugin ajouté ou retiré, piste renommée : renvoyer la liste sans attendre « Relire ».
    now = time.monotonic()
    if listed_tracks is not None and now >= next_list_check:
        next_list_check = now + 1.0
        tracks = used_tracks()
        if tracks != listed_tracks:
            send_list(tracks)
    while pending:
        send_track(pending.pop(), only_if_changed=True)
    send_selected()


def OnSysEx(event):
    data = bytes(event.sysex)
    if len(data) < 6 or data[:4] != HEADER or data[-1] != 0xF7:
        return
    event.handled = True
    if data[4] >= 0x40:
        # Une réponse du script revient dans sa propre entrée : un port est réglé sur la sortie « Mixdeck to FL ».
        # Ne jamais y répondre, sinon les messages d'erreur tournent en boucle.
        global echo_reported
        if not echo_reported:
            echo_reported = True
            print("Mixdeck : retirez le port de la sortie « Mixdeck to FL » dans Options > MIDI (boucle détectée).")
        return
    handle(data[4], list(data[5:-1]))
