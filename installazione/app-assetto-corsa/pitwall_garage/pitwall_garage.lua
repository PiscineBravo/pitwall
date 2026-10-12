--[[

  Pitwall Garage - il computer del box.

  E il monitor che in Formula 1 sta appoggiato al muretto e che il pilota guarda quando rientra:
  radar meteo, stato della vettura, tempi, telemetria e analisi del passo. Per la stessa ragione
  per cui esiste nella realta, qui si consulta solo da fermi ai box: in pista hai l'overlay
  piccolo, non un cruscotto di dati.

  I dati arrivano dal plugin SimHub attraverso Documents/Pitwall/live/state.json, letto due
  volte al secondo. Qui non si calcola nulla: tutto quello che si vede e stato deciso da
  Pitwall, questa e solo la vetrina.
]]

-- ---------------------------------------------------------------------------------------------
-- Le lingue
--
-- Le frasi stanno in pitwall_testi.lua, una riga per frase con italiano e inglese accanto. La
-- lingua la sceglie il Manager e arriva nel file che questa app gia legge: qui non si decide
-- niente, si obbedisce. Una chiave che non esiste torna indietro com e invece di sparire: una
-- scritta storta si vede e si corregge, una finestra vuota in mezzo a una gara no.
-- ---------------------------------------------------------------------------------------------

local FRASI = {}
pcall(function() FRASI = require('pitwall_testi') end)

local inInglese = false

local function T(chiave)
  local voce = FRASI[chiave]
  if voce == nil then return chiave end
  if inInglese and voce[2] ~= nil and voce[2] ~= '' then return voce[2] end
  return voce[1]
end


local REFRESH = 0.5

local data = nil
local sinceRead = 99
local sinceData = 99
local lastSequence = -1
local statePath = nil
local page = 1
local radarSweep = 0

--- Un numero, con un valore di riserva se non lo e.
---
--- Sta qui in cima e non piu in mezzo al file perche la usano anche le parti che vengono
--- prima dell interfaccia. Dichiarata dopo, per loro era una globale che vale nil: la
--- chiamata falliva dentro un pcall, in silenzio, e i comandi dai pulsanti di SimHub non
--- arrivavano mai.
local function num(v, fallback) return tonumber(v) or (fallback or 0) end

local PAGES = { 'METEO', 'STRATEGIA', 'TEMPI', 'AUTO', 'TELEMETRIA', 'ANALISI', 'HUD', 'IMPOSTAZIONI' }
local PAGE_COUNT = #PAGES - 1   -- l'ultima e la configurazione, fuori dal giro delle pagine

-- Palette da monitor di garage: fondo quasi nero, testo freddo, un solo accento acceso. I
-- monitor veri sono cosi perche stanno in un box illuminato male e vanno letti di sbieco, non
-- perche qualcuno abbia scelto un tema scuro.
local C = {
  bg       = rgbm(0.03, 0.04, 0.05, 0.96),
  panel    = rgbm(0.06, 0.08, 0.10, 1),
  header   = rgbm(0.08, 0.10, 0.13, 1),
  line     = rgbm(0.16, 0.20, 0.25, 1),
  edge     = rgbm(0.26, 0.32, 0.40, 1),
  text     = rgbm(0.88, 0.92, 0.96, 1),
  muted    = rgbm(0.46, 0.52, 0.60, 1),
  label    = rgbm(0.38, 0.44, 0.52, 1),
  accent   = rgbm(0.88, 0.05, 0.05, 1),
  screen   = rgbm(0.35, 0.85, 0.95, 1),   -- il ciano dei terminali di pista
  good     = rgbm(0.20, 0.82, 0.38, 1),
  warn     = rgbm(1.00, 0.72, 0.10, 1),
  bad      = rgbm(0.95, 0.25, 0.22, 1),
  cold     = rgbm(0.35, 0.65, 1.00, 1),
  rain     = rgbm(0.25, 0.55, 0.95, 1)
}

-- ---------------------------------------------------------------------------------------------
-- Dati
-- ---------------------------------------------------------------------------------------------

--- Custom Shaders Patch limita i percorsi che gli script delle app possono leggere, e quali
--- siano dipende dalla versione. Il plugin pubblica lo stato in piu posti e qui si prova ognuno
--- finche uno risponde; quello che funziona viene ricordato.
---
--- Tutto quello che succede qui viene annotato in `diag` e mostrato quando il collegamento non
--- riesce: senza, un fallimento silenzioso costringe a tirare a indovinare.
local diag = {
  ticks = 0,          -- quante volte ha girato l'aggiornamento di sistema
  windowTicks = 0,    -- quante volte ha girato il disegno della finestra
  io = nil,           -- l'accesso ai file e disponibile
  folders = nil,      -- le cartelle di sistema sono interrogabili
  attempts = {},      -- percorsi provati senza successo
  folderValues = {},  -- cosa ha risposto il gioco per ogni cartella richiesta
  lastError = nil,
  parseError = nil
}

local function probe()
  if diag.io ~= nil then return end
  diag.io = type(io) == 'table' and type(io.load) == 'function'
  diag.folders = type(ac.getFolder) == 'function' and ac.FolderID ~= nil
end

local function candidatePaths()
  local list = {}
  if not diag.folders then return list end

  -- Importante: quello che torna da ac.getFolder non e per forza una stringa Lua, puo essere
  -- un oggetto che la rappresenta. Controllarne il tipo scartava tutto in silenzio; qui lo si
  -- converte e basta.
  local function add(label, id, suffix)
    if id == nil then diag.folderValues[label] = 'id assente' return end

    local ok, base = pcall(ac.getFolder, id)
    if not ok then diag.folderValues[label] = 'errore: ' .. tostring(base) return end
    if base == nil then diag.folderValues[label] = 'nil' return end

    local text = tostring(base)
    diag.folderValues[label] = text
    if text ~= '' then list[#list + 1] = text .. suffix end
  end

  diag.folderValues = {}
  add('ACDocuments', ac.FolderID.ACDocuments, '\\Pitwall\\state.json')
  add('Documents', ac.FolderID.Documents, '\\Pitwall\\live\\state.json')
  add('Root', ac.FolderID.Root, '\\apps\\lua\\pitwall\\live\\state.json')
  return list
end

local function readState()
  probe()
  if not diag.io then return nil end

  if statePath then
    local raw = io.load(statePath, nil)
    if raw ~= nil and raw ~= '' then return raw end
    statePath = nil
  end

  diag.attempts = {}
  for _, path in ipairs(candidatePaths()) do
    local raw = io.load(path, nil)
    if raw ~= nil and raw ~= '' then
      statePath = path
      return raw
    end
    diag.attempts[#diag.attempts + 1] = path
  end
  return nil
end

local function tick(dt)
  sinceRead = sinceRead + dt
  sinceData = sinceData + dt
  radarSweep = (radarSweep + dt * 1.1) % (math.pi * 2)

  if sinceRead < REFRESH then return end
  sinceRead = 0

  local raw = readState()
  if raw == nil or raw == '' then return end

  -- Difesa contro il marcatore iniziale UTF-8: tre byte invisibili prima della graffa
  -- bastano a far fallire il parser senza dire niente.
  if raw:byte(1) == 239 and raw:byte(2) == 187 and raw:byte(3) == 191 then
    raw = raw:sub(4)
  end

  local ok, parsed = pcall(JSON.parse, raw)
  if not ok then
    diag.parseError = tostring(parsed)
    return
  end
  if type(parsed) == 'table' then
    -- Il file letto non vuol dire dati vivi: se il plugin si ferma, il file resta dov'e con
    -- dentro l'ultima fotografia. Conta solo che il contatore di pubblicazione sia salito.
    local seq = tonumber(parsed.Sequence) or -1
    if seq < 0 or seq ~= lastSequence then
      lastSequence = seq
      sinceData = 0
    end
    data = parsed

    -- La lingua arriva insieme a tutto il resto: il Manager la sceglie, qui si obbedisce.
    inInglese = parsed.Lingua == 'en'

    diag.parseError = nil
  else
    diag.parseError = 'il file e stato letto ma non e JSON valido (' .. type(parsed) .. ')'
  end
end

--- L'aggiornamento di sistema non viene chiamato in tutte le configurazioni, quindi la lettura
--- parte anche dal disegno della finestra. La soglia di tempo evita di leggere due volte.
local function safeTick(dt, fromWindow)
  if fromWindow then diag.windowTicks = diag.windowTicks + 1
  else diag.ticks = diag.ticks + 1 end

  local ok, err = pcall(tick, dt)
  if not ok then diag.lastError = tostring(err) end
end

-- ---------------------------------------------------------------------------------------------
-- Comandi: pulsanti fisici e scorciatoie
-- ---------------------------------------------------------------------------------------------

--- Scrive un comando per il plugin. Dichiarato qui perche serve ai pulsanti della strategia.
local canWrite = nil

local function sendCommand(line)
  local ok, base = pcall(ac.getFolder, ac.FolderID.ACDocuments)
  if not ok or base == nil then canWrite = false return end

  local target = tostring(base) .. '\\Pitwall\\command.txt'
  local saved = false
  local written = pcall(function() saved = io.save(target, line .. '\n') end)
  canWrite = written and saved ~= false
end

local function goToPage(index)
  if index >= 1 and index <= #PAGES then page = index end
end

--- I comandi dell'app, ognuno con il suo pulsante assegnabile.
---
--- Custom Shaders Patch li gestisce da solo: l'assegnazione finisce in controls.ini, insieme a
--- tutti gli altri comandi del gioco, e funziona con qualsiasi periferica — volante, pulsantiera,
--- tastiera. Il widget di assegnazione si disegna nella pagina Impostazioni.
local COMMANDS = {
  { id = 'PITWALL_GARAGE_NEXT',     label = 'Pagina successiva',   run = function() goToPage(page % PAGE_COUNT + 1) end },
  { id = 'PITWALL_GARAGE_PREV',     label = 'Pagina precedente',   run = function() goToPage((page - 2) % PAGE_COUNT + 1) end },
  { id = 'PITWALL_GARAGE_WEATHER',  label = 'Vai a Meteo',         run = function() goToPage(1) end },
  { id = 'PITWALL_GARAGE_STRATEGY', label = 'Vai a Strategia',     run = function() goToPage(2) end },
  { id = 'PITWALL_GARAGE_TIMING',   label = 'Vai a Tempi',         run = function() goToPage(3) end },
  { id = 'PITWALL_GARAGE_CAR',      label = 'Vai ad Auto',         run = function() goToPage(4) end },
  { id = 'PITWALL_GARAGE_TELE',     label = 'Vai a Telemetria',    run = function() goToPage(5) end },
  { id = 'PITWALL_GARAGE_ANALYSIS', label = 'Vai ad Analisi',      run = function() goToPage(6) end },
  { id = 'PITWALL_STRATEGY_A',      label = 'Scegli scenario A',   run = function() sendCommand('select-strategy A') end },
  { id = 'PITWALL_STRATEGY_B',      label = 'Scegli scenario B',   run = function() sendCommand('select-strategy B') end },
  { id = 'PITWALL_STRATEGY_C',      label = 'Scegli scenario C',   run = function() sendCommand('select-strategy C') end },
  { id = 'PITWALL_STRATEGY_D',      label = 'Scegli scenario D',   run = function() sendCommand('select-strategy D') end },
  { id = 'PITWALL_AUTOPILOT',       label = 'Pilota automatico',   run = function() sendCommand('autopilot') end }
}

local controlsReady = false

local function ensureControls()
  if controlsReady then return end
  controlsReady = true

  for _, c in ipairs(COMMANDS) do
    local ok, button = pcall(ac.ControlButton, c.id)
    if ok and button ~= nil then
      c.button = button
      -- Sempre attivi: il computer di garage si guarda da fermi, spesso col gioco in pausa
      -- o col menu dei box aperto.
      pcall(function() button:setAlwaysActive(true) end)
    end
  end
end

--- Legge i pulsanti e lancia i comandi.
---
--- Il fermo sullo stato precedente non e un di piu: questa funzione viene chiamata sia
--- dall'aggiornamento di sistema sia dal disegno della finestra, e il fronte di pressione
--- resta valido per tutto il fotogramma. Senza, una pressione varrebbe per due e le pagine
--- scorrerebbero a due a due.
local function pollControls()
  ensureControls()

  for _, c in ipairs(COMMANDS) do
    if c.button ~= nil then
      local down = false
      local ok = pcall(function() down = c.button:down() end)
      if ok then
        if down and not c.wasDown then c.run() end
        c.wasDown = down
      end
    end
  end
end

--- Comandi che arrivano da SimHub, cioe da un pulsante mappato nel plugin. Il numero
--- progressivo evita di rieseguire all'infinito l'ultimo comando ricevuto.
local lastUiSeq = -1

local function applyUiCommand()
  if data == nil then return end
  local seq = num(data.UiCommandSeq, -1)
  if seq == lastUiSeq then return end
  lastUiSeq = seq

  local command = tostring(data.UiCommand or '')
  if command == '' then return end

  if command == 'page:next' then goToPage(page % PAGE_COUNT + 1)
  elseif command == 'page:prev' then goToPage((page - 2) % PAGE_COUNT + 1)
  else
    local index = tonumber(command:match('^page:(%d+)$'))
    if index then goToPage(index) end
  end
end

-- ---------------------------------------------------------------------------------------------
-- Quello che sta succedendo davvero
--
-- Il piano meteo di Pitwall e una previsione: diventa realta solo se il controller meteo di
-- CSP e attivo. Finche non lo e, il piano dice pioggia e dal cockpit si vede il sole. Qui si
-- legge il simulatore, cosi il radar mostra sempre le condizioni vere e il piano resta quello
-- che e, una previsione.
-- ---------------------------------------------------------------------------------------------

local sim = { ok = false, rain = 0, wetness = 0, water = 0, air = 0, road = 0 }

--- Gomme viste dal simulatore, per indice vettura. CSP sa la mescola di tutti, cosa che la
--- telemetria di SimHub su Assetto Corsa non espone: i giri sulle gomme li conta questa app
--- guardando i cambi mescola e le uscite dai box.
local tyres = {}
local carByName = {}
local sinceCars = 99

--- Il controller meteo di CSP pubblica un battito: qui si guarda se arriva ancora. E l'unico
--- modo onesto di sapere se il piano viene applicato - prima lo si deduceva dal fatto che la
--- pista fosse asciutta, che e un indizio, non una prova.
local wfx = { beat = -1, silent = 99, running = false, hasPlan = false, source = '-', points = 0, rain = 0 }

local function readController(dt)
  local beat = tonumber(ac.load('.pitwall.wfx.beat')) or -1
  if beat ~= wfx.beat then
    wfx.beat = beat
    wfx.silent = 0
  else
    wfx.silent = wfx.silent + dt
  end

  wfx.running = beat >= 0 and wfx.silent < 3
  wfx.hasPlan = wfx.running and (tonumber(ac.load('.pitwall.wfx.plan')) or 0) > 0
  wfx.source = tostring(ac.load('.pitwall.wfx.source') or '-')
  wfx.points = tonumber(ac.load('.pitwall.wfx.points')) or 0
  wfx.rain = tonumber(ac.load('.pitwall.wfx.rain')) or 0
end

--- Come si chiama il tempo che c'e in pista adesso, dai valori del simulatore.
---
--- Serve perche il piano meteo e la pista sono due cose diverse: il piano e una previsione,
--- la pista e quello che vedi dal parabrezza. Mostrarli con la stessa etichetta dava la
--- schermata che mi e stata segnalata - "Condizioni: pioggia forte", "Acqua in pista: 0%",
--- "Gomma giusta: slick" - tre righe che si contraddicono e nessuna che dice qual e quella
--- da credere.
local function simWeatherLabel()
  if sim.rain >= 0.6 then return 'Pioggia forte' end
  if sim.rain >= 0.25 then return 'Pioggia' end
  if sim.rain > 0.02 then return 'Pioggia leggera' end
  if sim.wetness >= 0.4 then return 'Pista bagnata' end
  if sim.wetness > 0.05 then return 'Pista umida' end
  return 'Asciutto'
end

local function readSim()
  local s = ac.getSim()
  if s == nil then return end

  sim.rain = s.rainIntensity or 0
  sim.wetness = s.rainWetness or 0
  sim.water = s.rainWater or 0
  sim.air = s.ambientTemperature or 0
  sim.road = s.roadTemperature or 0
  sim.ok = true

  for i = 0, (s.carsCount or 1) - 1 do
    local car = ac.getCar(i)
    if car ~= nil then
      local t = tyres[i]
      if t == nil then
        t = { compound = -1, baseLap = car.lapCount or 0, age = 0, name = '', wasInPit = false }
        tyres[i] = t
      end

      -- Gomme nuove: o e cambiata la mescola, o la vettura e appena uscita dai box.
      local inPit = car.isInPitlane == true
      if car.compoundIndex ~= t.compound then
        t.compound = car.compoundIndex
        t.baseLap = car.lapCount or 0
        t.name = ac.getTyresName(i, -1) or ''
      elseif t.wasInPit and not inPit then
        t.baseLap = car.lapCount or 0
      end
      t.wasInPit = inPit
      t.age = math.max(0, (car.lapCount or 0) - t.baseLap)
    end
  end
end

--- I nomi dei piloti cambiano solo a inizio sessione: si rileggono ogni tanto, non ogni frame.
local function refreshCarNames(dt)
  sinceCars = sinceCars + dt
  if sinceCars < 5 then return end
  sinceCars = 0

  local s = ac.getSim()
  if s == nil then return end

  carByName = {}
  for i = 0, (s.carsCount or 1) - 1 do
    local n = ac.getDriverName(i)
    if n ~= nil and n ~= '' then carByName[n:lower()] = i end
  end
end

--- Gomma e giri di un pilota secondo il simulatore, nil se quel nome non e in pista.
local function tyresOf(name)
  if name == nil or name == '' then return nil end
  local index = carByName[tostring(name):lower()]
  if index == nil then return nil end
  return tyres[index]
end


--- Fermi nella propria piazzola box: e li, e solo li, che si guarda il computer di garage.
---
--- Due restrizioni, imparate una alla volta. La prima: contava anche essere semplicemente fermi,
--- e sulla griglia di partenza si e fermi - il computer compariva mentre si aspettava il
--- semaforo. La seconda: contava tutta la corsia box, e quindi si apriva mentre ci si passava
--- dentro in movimento. Adesso serve essere nella propria piazzola e immobili, come quando si
--- scende dalla macchina per guardare il monitor.
local function inGarage()
  local car = ac.getCar(0)
  if car == nil then return false end
  return car.isInPit == true and (car.speedKmh or 0) < 2
end

--- La finestra si apre quando rientri e si chiude quando esci, da sola: e un monitor del box,
--- non un pannello da portarsi in pista. Se la chiudi tu mentre sei ai box resta chiusa, e
--- torna disponibile al rientro successivo.
local WINDOW_ID = 'garage'
local garageOpen = nil
local closedByUser = false

local followTimer = 0

local function followGarage(dt)
  -- Un controllo due volte al secondo basta e avanza: chiamare setWindowOpen a ogni fotogramma
  -- sarebbe lavoro sprecato.
  followTimer = followTimer + dt
  if followTimer < 0.5 then return end
  followTimer = 0

  local here = inGarage()

  if not here then
    -- Fuori dai box la regola si applica sempre, non solo quando si esce: se qualcosa la
    -- riapre - il gioco che ricorda le finestre aperte, un clic per sbaglio - deve richiudersi.
    -- E un monitor del box, in pista non ci sta.
    closedByUser = false
    if ac.isWindowOpen(WINDOW_ID) then ac.setWindowOpen(WINDOW_ID, false) end
    garageOpen = false
    return
  end

  -- Ai box invece si apre una volta sola, al rientro. Se poi la chiudi tu resta chiusa fino
  -- al rientro successivo, altrimenti ti si riaprirebbe in faccia ogni mezzo secondo.
  if not garageOpen then
    garageOpen = true
    if not closedByUser then ac.setWindowOpen(WINDOW_ID, true) end
  elseif not ac.isWindowOpen(WINDOW_ID) then
    closedByUser = true
  end
end

function script.update(dt)
  safeTick(dt, false)
  -- Le gomme si seguono sempre, anche a finestra chiusa: se si perde l'uscita dai box di
  -- qualcuno, i giri sulle gomme restano sbagliati per tutto il resto della gara.
  pcall(readSim)
  pcall(readController, dt)
  pcall(refreshCarNames, dt)
  pcall(followGarage, dt)
  pcall(pollControls)
  pcall(applyUiCommand)
end

local function connected() return data ~= nil and sinceData < 6 end


-- ---------------------------------------------------------------------------------------------
-- Pezzi di interfaccia
-- ---------------------------------------------------------------------------------------------

--- Titolo di sezione: una fascia piena con la barretta d'accento a sinistra, come le intestazioni
--- dei pannelli sui monitor di pista. Squadrata, senza raccordi: niente qui e arrotondato.
local function header(title, right)
  local width = ui.availableSpaceX()
  local cursor = ui.getCursor()
  local h = 24

  ui.drawRectFilled(cursor, cursor + vec2(width, h), C.header)
  ui.drawRectFilled(cursor, cursor + vec2(3, h), C.accent)
  ui.drawLine(cursor + vec2(0, h), cursor + vec2(width, h), C.edge, 1)

  ui.dummy(vec2(0, 4))
  ui.sameLine(12)
  ui.textColored(title, C.text)

  if right then
    ui.sameLine()
    ui.pushFont(ui.Font.Monospace)
    local w = ui.measureText(right).x
    ui.sameLine(math.max(0, width - w - 10))
    ui.textColored(right, C.muted)
    ui.popFont()
  end

  ui.dummy(vec2(width, h - 14))
end

--- Etichetta e valore. Il valore in monospace e incolonnato: su un cruscotto i numeri devono
--- stare sempre nella stessa colonna, o l'occhio deve rileggerli ogni volta.
local function field(label, value, color)
  ui.textColored(label, C.label)
  ui.sameLine(160)
  ui.pushFont(ui.Font.Monospace)
  ui.textColored(value, color or C.text)
  ui.popFont()
end

local function field(label, value, color)
  ui.textColored(label, C.muted)
  ui.sameLine(160)
  ui.textColored(value, color or C.text)
end

--- Barra orizzontale con etichetta, usata per usure, pedali, serbatoio.
local function bar(label, value01, text, color)
  local width = math.min(260, ui.availableSpaceX() - 170)
  ui.textColored(label, C.muted)
  ui.sameLine(110)
  local cursor = ui.getCursor()
  local h = 14
  ui.drawRectFilled(cursor, cursor + vec2(width, h), rgbm(0.16, 0.18, 0.22, 1))
  local filled = math.saturate(value01) * width
  ui.drawRectFilled(cursor, cursor + vec2(filled, h), color or C.good)
  ui.dummy(vec2(width, h))
  ui.sameLine()
  ui.textColored(text or '', C.text)
end

local function weatherColor(weatherType)
  if weatherType >= 6 and weatherType <= 8 then return C.rain end          -- pioggia
  if weatherType == 19 then return rgbm(0.45, 0.47, 0.52, 1) end           -- coperto
  if weatherType == 17 or weatherType == 18 then return rgbm(0.60, 0.64, 0.70, 1) end
  if weatherType == 16 then return rgbm(0.72, 0.80, 0.90, 1) end
  return rgbm(0.35, 0.65, 0.95, 1)                                          -- sereno
end

-- ---------------------------------------------------------------------------------------------
-- METEO: il radar
-- ---------------------------------------------------------------------------------------------

--- Radar circolare: il centro e adesso, ogni anello sono quindici minuti avanti. I settori
--- colorati sono le fasi del piano meteo, quelli blu la pioggia in arrivo.
local function drawRadar(size)
  local cursor = ui.getCursor()
  local radius = size / 2
  local centre = cursor + vec2(radius, radius)
  local horizon = 45

  ui.drawCircleFilled(centre, radius, rgbm(0.05, 0.08, 0.07, 1), 48)

  for ring = 1, 3 do
    ui.drawCircle(centre, radius * ring / 3, rgbm(0.20, 0.35, 0.28, 1), 48, 1)
  end
  ui.drawLine(centre - vec2(radius, 0), centre + vec2(radius, 0), rgbm(0.20, 0.35, 0.28, 1), 1)
  ui.drawLine(centre - vec2(0, radius), centre + vec2(0, radius), rgbm(0.20, 0.35, 0.28, 1), 1)

  local timeline = data and data.WeatherTimeline or nil
  local now = num(data and data.SessionElapsedSeconds) / 60

  if timeline then
    for i = 1, #timeline do
      local p = timeline[i]
      local start = num(p.Minute) - now
      local stop = (i < #timeline) and (num(timeline[i + 1].Minute) - now) or horizon
      if stop > 0 and start < horizon then
        start = math.max(0, start)
        stop = math.min(horizon, stop)
        local rain = num(p.Rain)
        if rain > 0.01 then
          -- Una fascia di pioggia: quanto piu e vicina al centro, tanto prima arriva.
          local r0 = radius * (start / horizon)
          local r1 = radius * (stop / horizon)
          local intensity = math.min(1, 0.25 + rain * 1.2)
          local color = rgbm(0.15, 0.45 + rain * 0.3, 0.95, intensity * 0.75)
          local steps = 26
          for s = 0, steps - 1 do
            local a0 = (s / steps) * math.pi * 2
            local a1 = ((s + 1) / steps) * math.pi * 2
            ui.drawLine(centre + vec2(math.cos(a0), math.sin(a0)) * r0,
                        centre + vec2(math.cos(a1), math.sin(a1)) * r1, color, math.max(2, (r1 - r0) / 2))
          end
        end
      end
    end
  end

  -- La spazzata, che gira come su un radar vero.
  local sweepEnd = centre + vec2(math.cos(radarSweep), math.sin(radarSweep)) * radius
  ui.drawLine(centre, sweepEnd, rgbm(0.35, 0.95, 0.5, 0.75), 2)
  ui.drawCircleFilled(centre, 4, rgbm(0.35, 0.95, 0.5, 1), 12)

  ui.dummy(vec2(size, size))
end

--- Striscia temporale della sessione, con il tempo che cambia e il segnalino su adesso.
local function drawTimeline(width, height)
  local cursor = ui.getCursor()
  local total = math.max(10, num(data and data.SessionLengthMinutes, 60))
  local now = num(data and data.SessionElapsedSeconds) / 60
  local timeline = data and data.WeatherTimeline or nil

  ui.drawRectFilled(cursor, cursor + vec2(width, height), rgbm(0.12, 0.14, 0.17, 1))

  if timeline then
    for i = 1, #timeline do
      local p = timeline[i]
      local from = num(p.Minute)
      local to = (i < #timeline) and num(timeline[i + 1].Minute) or total
      local x0 = cursor.x + width * math.saturate(from / total)
      local x1 = cursor.x + width * math.saturate(to / total)
      ui.drawRectFilled(vec2(x0, cursor.y), vec2(x1, cursor.y + height), weatherColor(math.floor(num(p.Type, 15))))
    end
  end

  local marker = cursor.x + width * math.saturate(now / total)
  ui.drawLine(vec2(marker, cursor.y - 3), vec2(marker, cursor.y + height + 3), rgbm(1, 1, 1, 0.95), 2)

  ui.dummy(vec2(width, height + 6))
end

--- Previsione a 60 minuti: intensita della pioggia come profilo pieno, temperatura dell'aria
--- come linea, e il segnalino su adesso. E la lettura che conta davvero per decidere quando
--- cambiare gomme: non "pioveva", ma "quanto, e fra quanto".
local function drawRainChart(width, height)
  local cursor = ui.getCursor()
  local horizon = 60
  local timeline = data.WeatherTimeline
  local now = num(data.SessionElapsedSeconds) / 60

  ui.drawRectFilled(cursor, cursor + vec2(width, height), rgbm(0.09, 0.11, 0.14, 1))

  -- Griglia ogni dieci minuti.
  for m = 0, horizon, 10 do
    local x = cursor.x + width * (m / horizon)
    ui.drawLine(vec2(x, cursor.y), vec2(x, cursor.y + height), rgbm(0.18, 0.21, 0.26, 1), 1)
  end

  if timeline and #timeline > 0 then
    local function sample(minute)
      local rain, air = 0, 20
      for i = 1, #timeline do
        if num(timeline[i].Minute) <= minute then
          rain = num(timeline[i].Rain)
          air = num(timeline[i].AirTemperature, 20)
        end
      end
      return rain, air
    end

    local steps = 60
    local prevX, prevY
    for s = 0, steps do
      local minute = now + horizon * (s / steps)
      local rain, air = sample(minute)
      local x = cursor.x + width * (s / steps)

      if rain > 0.001 then
        local h = math.min(1, rain / 0.8) * (height - 18)
        ui.drawRectFilled(vec2(x - width / steps / 2, cursor.y + height - h),
                          vec2(x + width / steps / 2, cursor.y + height),
                          rgbm(0.15, 0.45 + rain * 0.35, 0.95, 0.55 + rain * 0.4))
      end

      -- Temperatura su scala 0-45 gradi, per avere un riferimento stabile fra sessioni.
      local y = cursor.y + height - math.saturate(air / 45) * (height - 6) - 3
      if prevX then ui.drawLine(vec2(prevX, prevY), vec2(x, y), rgbm(1, 0.72, 0.2, 0.9), 2) end
      prevX, prevY = x, y
    end
  end

  ui.drawLine(vec2(cursor.x, cursor.y), vec2(cursor.x, cursor.y + height), rgbm(1, 1, 1, 0.9), 2)
  ui.dummy(vec2(width, height + 4))

  local labels = ''
  for m = 0, horizon, 10 do labels = labels .. string.format('%-11s', '+' .. m) end
  ui.textColored(labels, C.muted)
end

local function pageWeather()
  header(T('ac.radar.meteo'), string.format('sessione %.0f min', num(data.SessionLengthMinutes, 0)))

  ui.columns(2, false)
  ui.setColumnWidth(0, 260)

  drawRadar(230)
  ui.textColored(T('ac.centro.adesso'), C.muted)
  ui.textColored(T('ac.anelli.15.30.45.min'), C.muted)

  ui.nextColumn()

  local rainIn = num(data.MinutesToRain, -1)

  -- Il pannello "adesso" dice quello che il simulatore ha davvero sotto le ruote. Il piano di
  -- Pitwall serve per la previsione, non per dichiarare bagnata una pista asciutta.
  local wetness = sim.ok and sim.wetness or num(data.Wetness)
  local air = sim.ok and sim.air or num(data.AirTemperature)
  local road = sim.ok and sim.road or num(data.RoadTemperature)

  if rainIn >= 0 then
    ui.pushFont(ui.Font.Title)
    ui.textColored(string.format(T('ac.pioggia.fra.min'), rainIn), C.rain)
    ui.popFont()
  elseif wetness > 0.05 then
    ui.pushFont(ui.Font.Title)
    ui.textColored(T('ac.pista.bagnata'), C.rain)
    ui.popFont()
  else
    ui.pushFont(ui.Font.Title)
    ui.textColored(T('ac.asciutto'), C.good)
    ui.popFont()
  end

  ui.dummy(vec2(1, 6))

  -- Quello che c'e in pista, misurato. E la riga che comanda, perche e quella che decide
  -- come si guida e che gomma ci vuole.
  local onTrack = sim.ok and simWeatherLabel() or tostring(data.WeatherNow or '-')
  field(T('ac.condizioni'), onTrack)

  -- Il piano si mostra solo quando dice qualcosa di diverso, e detto per quello che e: una
  -- previsione che la pista deve ancora raggiungere, non una contraddizione.
  local planned = tostring(data.WeatherNow or '')
  if sim.ok and planned ~= '' and planned ~= onTrack then
    field(T('ac.da.piano'), planned .. ' (previsto)', C.warn)
  end

  field(T('ac.aria'), string.format('%.1f C', air))
  field(T('ac.pista'), string.format('%.1f C', road))
  field(T('ac.acqua.in.pista'), string.format('%.0f%%', wetness * 100), wetness > 0.05 and C.rain or C.text)
  field(T('ac.gomma.giusta'), wetness >= 0.4 and 'Full wet' or (wetness >= 0.12 and 'Intermedie' or 'Slick'),
        wetness >= 0.12 and C.rain or C.good)

  -- Se il piano dice pioggia e dal parabrezza si vede il sole, qualcosa fra Pitwall e il cielo
  -- non sta funzionando. Il controller dice lui quale pezzo, invece di farlo indovinare.
  if sim.ok and num(data.Wetness) >= 0.2 and sim.wetness < 0.05 then
    local reason
    if not wfx.running then
      reason = 'il controller meteo di Pitwall non sta girando in Custom Shaders Patch. ' ..
               'Apri il Manager, scheda Circuiti, e premi "Attiva meteo dinamico", poi riavvia ' ..
               'Assetto Corsa: la scelta del controller si legge solo all\'avvio.'
    elseif not wfx.hasPlan then
      reason = 'il controller meteo gira ma non riceve il piano da Pitwall. Controlla che ' ..
               'SimHub sia aperto con il plugin attivo e una sessione in corso.'
    else
      reason = 'il controller sta applicando il piano, la pista ci sta ancora arrivando.'
    end

    ui.dummy(vec2(1, 6))
    ui.pushTextWrapPosition(0)
    ui.textColored((wfx.hasPlan and 'IN ARRIVO - ' or 'PIANO NON APPLICATO - ') .. reason,
                   wfx.hasPlan and C.warn or C.bad)
    ui.popTextWrapPosition()
  end

  -- Stato del controller, sempre a vista. Il meteo di Pitwall passa per due pezzi - il piano e
  -- chi lo applica - e se uno dei due manca si deve poter vedere a colpo d'occhio quale.
  ui.dummy(vec2(1, 6))
  if not wfx.running then
    field(T('ac.controller'), 'non attivo', C.bad)
  elseif not wfx.hasPlan then
    field(T('ac.controller'), 'attivo, senza piano', C.warn)
  else
    field(T('ac.controller'), string.format('attivo - %d punti da %s', wfx.points, wfx.source), C.good)
  end

  ui.dummy(vec2(1, 8))
  ui.pushTextWrapPosition(0)
  ui.textColored(tostring(data.WeatherForecast or ''), C.muted)
  ui.popTextWrapPosition()

  ui.columns(1)
  ui.dummy(vec2(1, 12))

  ui.textColored(T('ac.previsione.60.minuti.blu.intensita'), C.muted)
  drawRainChart(ui.availableSpaceX(), 96)

  ui.dummy(vec2(1, 8))
  ui.textColored(T('ac.andamento.della.sessione'), C.muted)
  drawTimeline(ui.availableSpaceX(), 20)

  local timeline = data.WeatherTimeline
  if timeline then
    local shown = 0
    for i = 1, #timeline do
      local p = timeline[i]
      local minute = num(p.Minute)
      if minute >= (num(data.SessionElapsedSeconds) / 60) - 5 and shown < 5 then
        shown = shown + 1
        ui.textColored(string.format(T('ac.min.pioggia.aria.c.pista'),
          minute, tostring(p.Label or ''), num(p.Rain) * 100,
          num(p.AirTemperature), num(p.RoadTemperature)), C.muted)
      end
    end
  end
end

-- ---------------------------------------------------------------------------------------------
-- AUTO
-- ---------------------------------------------------------------------------------------------

local CORNERS = { 'Ant. sinistra', 'Ant. destra', 'Post. sinistra', 'Post. destra' }

local function pageCar()
  local car = data.Car or {}
  header(T('ac.vettura'), tostring(data.TyreCompound or ''))

  ui.columns(2, false)

  ui.textColored(T('ac.gomme'), C.muted)
  local wear = car.TyreWear or {}
  for i = 1, 4 do
    local w = num(wear[i])
    local color = w > 75 and C.bad or (w > 50 and C.warn or C.good)
    bar(CORNERS[i], w / 100, string.format('%.0f%%', w), color)
  end
  ui.dummy(vec2(1, 6))
  field(T('ac.pressione'), string.format('%.1f', num(car.TyrePressure)))
  field(T('ac.temperatura'), string.format('%.0f C', num(car.TyreTemperature)))
  field(T('ac.giri.allo.stint'), tostring(num(data.StintLaps)))
  field(T('ac.al.degrado'), string.format('%.1f giri', num(data.LapsToCliff)),
        num(data.LapsToCliff) < 3 and C.bad or C.text)

  ui.nextColumn()

  ui.textColored(T('ac.motore.e.impianti'), C.muted)
  field(T('ac.benzina'), string.format('%.1f l', num(car.FuelLitres)))
  field(T('ac.consumo'), string.format('%.2f l/giro', num(car.FuelPerLap)))
  field(T('ac.autonomia'), string.format('%.1f giri', num(car.FuelLapsLeft)),
        num(car.FuelLapsLeft) < num(data.LapsRemaining) and C.bad or C.good)
  ui.dummy(vec2(1, 6))
  field(T('ac.acqua'), string.format('%.0f C', num(car.WaterTemperature)))
  field(T('ac.olio'), string.format('%.0f C', num(car.OilTemperature)))
  field(T('ac.freni'), string.format('%.0f C', num(car.BrakeTemperature)))
  ui.dummy(vec2(1, 6))
  field(T('ac.ers'), string.format('%.0f%%', num(car.ErsPercent)))
  field(T('ac.danni'), string.format('%.1f%%', num(car.DamagePercent)),
        num(car.DamagePercent) > 10 and C.bad or C.good)

  ui.columns(1)
end

-- ---------------------------------------------------------------------------------------------
-- STRATEGIA
-- ---------------------------------------------------------------------------------------------

local RISK_LABEL = { [1] = 'sicuro', [2] = 'da seguire', [3] = 'scommessa' }

local function pageStrategy()
  local options = data.StrategyOptions or {}
  header(T('ac.strategia'), #options > 0 and (#options .. ' scenari') or '')

  if #options == 0 then
    ui.textColored(T('ac.nessuno.scenario.gli.scenari.si'), C.muted)
    return
  end

  ui.textColored(T('ac.il.muretto.propone.tu.scegli'), C.muted)
  ui.dummy(vec2(1, 8))

  for _, o in ipairs(options) do
    local selected = o.Selected == true
    local recommended = o.Recommended == true
    local risk = num(o.Risk, 1)
    local riskColor = risk >= 3 and C.bad or (risk == 2 and C.warn or C.good)

    local cursor = ui.getCursor()
    local width = ui.availableSpaceX()
    local height = 76

    ui.drawRectFilled(cursor, cursor + vec2(width, height),
                      selected and rgbm(0.18, 0.13, 0.13, 1) or C.panel)
    if selected then
      ui.drawRectFilled(cursor, cursor + vec2(4, height), C.accent)
    elseif recommended then
      ui.drawRectFilled(cursor, cursor + vec2(4, height), C.good)
    end

    ui.setCursor(cursor + vec2(14, 8))
    ui.pushFont(ui.Font.Title)
    ui.textColored(o.Id .. '   ' .. tostring(o.Name or ''), selected and C.text or C.text)
    ui.popFont()

    ui.setCursor(cursor + vec2(14, 34))
    ui.textColored(tostring(o.Summary or ''), C.muted)

    ui.setCursor(cursor + vec2(14, 52))
    local delta = num(o.DeltaSeconds)
    local deltaText = delta <= 0.05 and 'il piu veloce' or string.format('+%.1f s', delta)
    ui.textColored(string.format(T('ac.soste.arrivo.previsto.p'),
      deltaText, num(o.Stops), num(o.ProjectedPosition), tostring(o.Plan or '')),
      delta <= 0.05 and C.good or C.text)

    ui.setCursor(cursor + vec2(width - 250, 34))
    ui.textColored(RISK_LABEL[risk] or '', riskColor)
    ui.setCursor(cursor + vec2(width - 250, 52))
    ui.textColored(tostring(o.RiskNote or ''), C.muted)

    ui.setCursor(cursor + vec2(width - 90, 12))
    if selected then
      ui.textColored(T('ac.scelto'), C.accent)
    elseif recommended then
      ui.textColored(T('ac.consigliato'), C.good)
    end

    ui.setCursor(cursor + vec2(width - 90, 40))
    if ui.button('Scegli ' .. o.Id, vec2(78, 26)) then
      sendCommand('select-strategy ' .. o.Id)
    end

    ui.setCursor(cursor + vec2(0, height + 8))
  end

  if canWrite == false then
    ui.dummy(vec2(1, 6))
    ui.textColored(T('ac.da.qui.non.posso.scrivere') ..
                   '(SelectStrategyA..D) o il gestionale.', C.warn)
  end
end

-- ---------------------------------------------------------------------------------------------
-- TEMPI
-- ---------------------------------------------------------------------------------------------

local function rankColor(rank)
  if rank == 2 then return rgbm(0.72, 0.35, 0.95, 1) end   -- viola: miglior tempo di sessione
  if rank == 1 then return C.good end                       -- verde: proprio personale
  return C.text
end

--- Le colonne della tabella dei tempi.
---
--- Una definizione sola, usata dall'intestazione e dalle righe. Prima erano due e diverse:
--- l'intestazione si allineava riempiendo di spazi con string.format, le righe si allineavano
--- con posizioni in pixel scritte a mano. Due sistemi che non parlano la stessa lingua non
--- possono combaciare, e infatti le intestazioni stavano da una parte e i numeri dall'altra.
---
--- Le larghezze sono frazioni dello spazio disponibile, non pixel: la finestra si puo
--- allargare e la tabella la segue. La somma dei pesi fa uno.
local TIMING_COLUMNS = {
  { title = 'POS',       weight = 0.042, align = 'right' },
  { title = '+/-',       weight = 0.044, align = 'center' },
  { title = 'PILOTA',    weight = 0.196 },
  { title = 'DISTACCO',  weight = 0.101, align = 'right' },
  { title = 'INTERV.',   weight = 0.101, align = 'right' },
  { title = 'ULT. GIRO', weight = 0.111, align = 'right' },
  { title = 'S1',        weight = 0.085, align = 'right' },
  { title = 'S2',        weight = 0.085, align = 'right' },
  { title = 'S3',        weight = 0.085, align = 'right' },
  { title = 'GOM',       weight = 0.058, align = 'center' },
  { title = 'ETA',       weight = 0.049, align = 'right' },
  { title = 'ST',        weight = 0.043, align = 'center' },
}

--- Calcola dove comincia e quanto e larga ogni colonna, per questa larghezza di finestra.
local function timingLayout(width)
  local usable = math.max(560, width - 8)
  local columns, x = {}, 0

  for i, col in ipairs(TIMING_COLUMNS) do
    local w = usable * col.weight
    columns[i] = { title = col.title, align = col.align, x = x, width = w }
    x = x + w
  end

  return columns
end

--- Scrive una cella nella sua colonna, rispettando l'allineamento.
---
--- I numeri vanno a destra. In una tabella di tempi e distacchi e l'unico allineamento che
--- si legge: le unita stanno una sopra l'altra e un decimo di differenza si vede senza
--- leggere la cifra. I nomi invece vanno a sinistra, come si legge un nome.
local function timingCell(col, text, colour, first)
  text = tostring(text or '')

  local offset = col.x
  if text ~= '' then
    if col.align == 'right' then
      offset = col.x + col.width - ui.measureText(text).x - 6
    elseif col.align == 'center' then
      offset = col.x + (col.width - ui.measureText(text).x) / 2
    end
  end

  -- La prima cella apre la riga. Non puo usare sameLine da sola, che proseguirebbe la riga
  -- precedente: prima serve un elemento qualsiasi che la apra, e un punto largo un pixel
  -- basta.
  if first then ui.dummy(vec2(1, 0)) end
  ui.sameLine(math.max(1, offset))

  ui.textColored(text, colour)
end

local function pageTiming()
  header(T('ac.tempi'), string.format('giro %d / %d', num(data.LapCurrent), num(data.LapsTotal)))

  local columns = timingLayout(ui.availableSpaceX())

  for i, col in ipairs(columns) do
    timingCell(col, col.title, C.muted, i == 1)
  end
  ui.separator()

  local rows = data.Timing or {}
  for i = 1, math.min(#rows, 24) do
    local r = rows[i]
    local base = r.IsPlayer and C.warn or C.text
    local change = num(r.PositionChange)

    -- Mescola e giri sulle gomme li sa il simulatore, non la telemetria: su Assetto Corsa
    -- SimHub non espone le gomme degli avversari, CSP si. Il dato di Pitwall resta come
    -- riserva, per i nomi che in pista non ci sono.
    local worn = tyresOf(r.Name)
    local compound = worn and worn.name ~= '' and worn.name or tostring(r.Compound or '')
    local age = worn and worn.age or num(r.TyreAge)

    local changeText = change > 0 and string.format('+%d', change)
                    or (change < 0 and tostring(change) or '-')
    local changeColour = change > 0 and C.good or (change < 0 and C.bad or C.muted)

    timingCell(columns[1],  num(r.Position), base, true)
    timingCell(columns[2],  changeText, changeColour)
    timingCell(columns[3],  r.Name, base)
    timingCell(columns[4],  r.Gap, C.muted)
    timingCell(columns[5],  r.Interval, base)
    timingCell(columns[6],  r.LastLap, rankColor(num(r.LastLapRank)))
    timingCell(columns[7],  r.S1, rankColor(num(r.S1Rank)))
    timingCell(columns[8],  r.S2, rankColor(num(r.S2Rank)))
    timingCell(columns[9],  r.S3, rankColor(num(r.S3Rank)))
    timingCell(columns[10], compound, base)
    timingCell(columns[11], age > 0 and age or '', age > 25 and C.warn or C.muted)
    timingCell(columns[12], r.InPit and 'BOX' or tostring(num(r.PitStops)),
               r.InPit and C.warn or C.muted)
  end

  ui.separator()
  ui.textColored(T('ac.viola.miglior.tempo.di.sessione'),
                 C.muted)
end

-- ---------------------------------------------------------------------------------------------
-- TELEMETRIA
-- ---------------------------------------------------------------------------------------------

local function drawLapChart(width, height)
  local laps = data.Car and data.Car.RecentLapSeconds or nil
  local cursor = ui.getCursor()
  ui.drawRectFilled(cursor, cursor + vec2(width, height), rgbm(0.10, 0.12, 0.15, 1))

  if laps and #laps >= 2 then
    local best, worst = laps[1], laps[1]
    for _, v in ipairs(laps) do
      if v < best then best = v end
      if v > worst then worst = v end
    end
    local span = math.max(0.4, worst - best)

    -- I giri arrivano dal piu recente: si disegnano al contrario, cosi il tempo scorre
    -- da sinistra a destra come ci si aspetta.
    local n = #laps
    local step = width / math.max(1, n - 1)
    for i = 1, n - 1 do
      local v0 = laps[n - i + 1]
      local v1 = laps[n - i]
      local x0 = cursor.x + (i - 1) * step
      local x1 = cursor.x + i * step
      local y0 = cursor.y + height - ((v0 - best) / span) * (height - 8) - 4
      local y1 = cursor.y + height - ((v1 - best) / span) * (height - 8) - 4
      ui.drawLine(vec2(x0, y0), vec2(x1, y1), C.warn, 2)
    end
  end

  ui.dummy(vec2(width, height + 4))
end

local function pageTelemetry()
  local car = data.Car or {}
  header(T('ac.telemetria'), string.format('%.0f km/h', num(car.SpeedKmh)))

  bar('Gas', num(car.Throttle), string.format('%.0f%%', num(car.Throttle) * 100), C.good)
  bar('Freno', num(car.Brake), string.format('%.0f%%', num(car.Brake) * 100), C.bad)
  ui.dummy(vec2(1, 6))
  field(T('ac.marcia'), tostring(car.Gear or 'N'))
  field(T('ac.giri.motore'), string.format('%.0f', num(car.Rpm)))
  field(T('ac.drs'), num(car.DrsOpen) > 0 and 'aperto' or (num(car.DrsAvailable) > 0 and 'disponibile' or 'chiuso'),
        num(car.DrsOpen) > 0 and C.good or C.muted)

  ui.dummy(vec2(1, 10))
  ui.textColored(T('ac.ultimi.giri'), C.muted)
  drawLapChart(ui.availableSpaceX(), 110)

  local laps = car.RecentLapSeconds or {}
  local line = ''
  for i = 1, math.min(#laps, 8) do
    line = line .. string.format('%7.3f', laps[i])
  end
  ui.textColored(line ~= '' and line or 'nessun giro completato', C.muted)
end

-- ---------------------------------------------------------------------------------------------
-- ANALISI
-- ---------------------------------------------------------------------------------------------

local function pageAnalysis()
  local car = data.Car or {}
  header(T('ac.analisi'), tostring(data.SessionLabel or ''))

  ui.columns(2, false)

  ui.textColored(T('ac.passo'), C.muted)
  field(T('ac.miglior.giro'), tostring(data.BestLap or '-'))
  field(T('ac.ultimo.giro'), tostring(data.LastLap or '-'))
  field(T('ac.passo.medio'), num(car.AveragePaceSeconds) > 0
        and string.format('%.3f s', num(car.AveragePaceSeconds)) or '-')
  field(T('ac.gestione'), num(car.PaceDeltaSeconds) > 0
        and string.format('+%.3f s dal migliore', num(car.PaceDeltaSeconds)) or '-',
        num(car.PaceDeltaSeconds) > 1.0 and C.warn or C.text)

  ui.dummy(vec2(1, 10))
  ui.textColored(T('ac.strategia'), C.muted)
  ui.pushTextWrapPosition(0)
  ui.textColored(tostring(data.StrategyAdvice or '-'), C.text)
  ui.popTextWrapPosition()
  field(T('ac.prossima.mescola'), tostring(data.RecommendedCompound or '-'))
  field(T('ac.finestra'), string.format('giri %d - %d (ideale %d)',
        num(data.PitWindowEarliest), num(data.PitWindowLatest), num(data.PitWindowIdeal)))

  ui.nextColumn()

  ui.textColored(T('ac.direzione.gara'), C.muted)
  field(T('ac.stato'), tostring(data.RaceControlText or '-'))
  field(T('ac.penalita'), num(data.PendingPenaltySeconds) > 0
        and string.format('+%.0f s', num(data.PendingPenaltySeconds)) or 'nessuna',
        num(data.PendingPenaltySeconds) > 0 and C.bad or C.good)
  field(T('ac.track.limits'), tostring(num(data.TrackLimitStrikes)))
  field(T('ac.safety.car'), string.format('%d   VSC %d', num(data.SafetyCarPeriods), num(data.VirtualSafetyCarPeriods)))

  ui.dummy(vec2(1, 10))
  ui.textColored(T('ac.strategia.avversari'), C.muted)
  local strategies = data.AiStrategy or {}
  for i = 1, math.min(#strategies, 10) do
    ui.textColored('  ' .. tostring(strategies[i]), C.muted)
  end

  ui.columns(1)
end

-- ---------------------------------------------------------------------------------------------
-- IMPOSTAZIONI: assegnazione dei pulsanti
-- ---------------------------------------------------------------------------------------------

-- ---------------------------------------------------------------------------------------------
-- I pannelli del cruscotto
--
-- Il cruscotto e un'altra app, quindi da qui non si possono aprire e chiudere le sue finestre
-- direttamente: ogni app comanda solo le proprie. Si passa per lo spazio condiviso fra script,
-- che e fatto apposta per questo - qui si scrive cosa si vuole, di la lo si esegue. E la stessa
-- strada che usiamo per passare il piano meteo al controller di Custom Shaders Patch.
-- ---------------------------------------------------------------------------------------------

local HUD_PANELS = {
  { id = 'standings', name = 'Classifica',  note = 'Posizioni, distacchi, gomme' },
  { id = 'map',       name = 'Mappa',       note = 'Tracciato con tutte le vetture' },
  { id = 'delta',     name = 'Delta',       note = 'Guadagno o perdita sul giro' },
  { id = 'ers',       name = 'Energia',     note = 'ERS, DRS e carburante' },
  { id = 'pit',       name = 'Ingegnere',   note = 'Radio, gomme e scenari di strategia' }
}

local function hudWanted(id)
  local v = ac.load('.pitwall.hud.' .. id)
  if v == nil then return true end        -- mai deciso: acceso
  return tonumber(v) == 1
end

local function pageHud()
  header(T('ac.pannelli.del.cruscotto'), connected() and 'cruscotto attivo' or 'cruscotto non in ascolto')

  ui.pushTextWrapPosition(0)
  ui.textColored(T('ac.accendi.e.spegni.i.pannelli') ..
                 'dimensione di ognuno si regolano trascinandoli in pista: restano come li metti.',
                 C.muted)
  ui.popTextWrapPosition()
  ui.dummy(vec2(1, 10))

  for _, p in ipairs(HUD_PANELS) do
    local on = hudWanted(p.id)
    local width = ui.availableSpaceX()
    local cursor = ui.getCursor()
    local h = 34

    local clicked = ui.invisibleButton('pitwall_hud_' .. p.id, vec2(width, h))
    local hovered = ui.itemHovered()

    ui.drawRectFilled(cursor, cursor + vec2(width, h),
                      hovered and C.header or C.panel)
    ui.drawRect(cursor, cursor + vec2(width, h), C.line, 0, 0, 1)
    ui.drawRectFilled(cursor, cursor + vec2(4, h), on and C.good or rgbm(0.25, 0.27, 0.30, 1))

    ui.drawText(p.name, cursor + vec2(14, 6), on and C.text or C.label)
    ui.pushFont(ui.Font.Small)
    ui.drawText(p.note, cursor + vec2(14, 20), C.label)

    local label = on and 'ACCESO' or 'spento'
    local lw = ui.measureText(label).x
    ui.drawText(label, cursor + vec2(width - lw - 12, 12), on and C.good or C.label)
    ui.popFont()

    ui.dummy(vec2(width, h + 4))

    if clicked then ac.store('.pitwall.hud.' .. p.id, on and 0 or 1) end
  end

  ui.dummy(vec2(1, 8))
  local w = ui.availableSpaceX()
  if ui.button('Accendi tutti', vec2(w / 2 - 4, 28)) then
    for _, p in ipairs(HUD_PANELS) do ac.store('.pitwall.hud.' .. p.id, 1) end
  end
  ui.sameLine()
  if ui.button('Spegni tutti', vec2(w / 2 - 4, 28)) then
    for _, p in ipairs(HUD_PANELS) do ac.store('.pitwall.hud.' .. p.id, 0) end
  end
end

local function pageSettings()
  header(T('ac.comandi'), 'volante, pulsantiera o tastiera')

  ui.pushTextWrapPosition(0)
  ui.textColored(T('ac.clicca.su.un.comando.e') ..
                 'periferica: l\'assegnazione la gestisce Assetto Corsa e finisce in controls.ini, ' ..
                 'insieme a tutti gli altri comandi del gioco.', C.muted)
  ui.popTextWrapPosition()
  ui.dummy(vec2(1, 10))

  ensureControls()

  for _, c in ipairs(COMMANDS) do
    ui.textColored(c.label, C.text)
    ui.sameLine(230)

    if c.button == nil then
      ui.textColored(T('ac.assegnazione.non.disponibile.in.questa'), C.warn)
    else
      local ok = pcall(function()
        c.button:control(vec2(190, 24), ui.ControlButtonControlFlags.None, 'clicca per assegnare')
      end)
      if not ok then ui.textColored(T('ac.non.disponibile'), C.warn) end

      ui.sameLine(440)
      local bound = nil
      pcall(function() bound = c.button:boundTo() end)
      ui.textColored(bound or 'nessun pulsante', bound and C.good or C.muted)
    end
  end

  ui.dummy(vec2(1, 14))
  ui.separator()
  ui.textColored(T('ac.da.simhub'), C.muted)
  ui.pushTextWrapPosition(0)
  ui.textColored(T('ac.gli.stessi.comandi.esistono.anche') ..
                 'tutte le mappature in SimHub: GarageNextPage, GaragePrevPage, GaragePageWeather, ' ..
                 'GaragePageStrategy, GaragePageTiming, GaragePageCar, GaragePageTelemetry, ' ..
                 'GaragePageAnalysis, SelectStrategyA..D. Le due strade convivono.', C.muted)
  ui.popTextWrapPosition()

  if canWrite == false then
    ui.dummy(vec2(1, 8))
    ui.textColored(T('ac.nota.da.qui.non.riesco') ..
                   'SimHub oppure il gestionale.', C.warn)
  end
end

-- ---------------------------------------------------------------------------------------------
-- Finestra
-- ---------------------------------------------------------------------------------------------

-- ---------------------------------------------------------------------------------------------
-- La cornice
--
-- Quello che fa sembrare un monitor di garage un monitor di garage non sono i colori: e il
-- fatto che abbia una cornice fissa. Una riga in alto che dice sempre dove sei, una fila di
-- pagine numerate, e una riga in basso con i comandi. Il contenuto cambia, la cornice no, e
-- l'occhio impara dove guardare invece di dover cercare ogni volta.
-- ---------------------------------------------------------------------------------------------

--- La riga in alto: scuderia, round e sessione, orologio e spia di collegamento.
local function chromeTop(width)
  local cursor = ui.getCursor()
  local h = 30

  ui.drawRectFilled(cursor, cursor + vec2(width, h), C.header)
  ui.drawLine(cursor + vec2(0, h), cursor + vec2(width, h), C.accent, 2)

  ui.dummy(vec2(0, 6))
  ui.sameLine(10)
  ui.textColored(T('ac.pitwall'), C.accent)

  ui.sameLine(84)
  local team = data and tostring(data.PlayerTeam or '') or ''
  ui.textColored(team ~= '' and team:upper() or 'GARAGE', C.text)

  -- Al centro, dove sei: e la domanda a cui un monitor di box risponde per primo.
  local middle = data and string.format('%s  -  %s',
    tostring(data.RoundName or '-'), tostring(data.SessionLabel or '-')) or '-'
  ui.pushFont(ui.Font.Monospace)
  local mw = ui.measureText(middle).x
  ui.sameLine(math.max(260, (width - mw) / 2))
  ui.textColored(middle, C.muted)
  ui.popFont()

  -- La spia: verde se i dati arrivano, rossa se il filo e staccato.
  local live = connected()
  local dot = cursor + vec2(width - 16, h / 2)
  ui.drawCircleFilled(dot, 4, live and C.good or C.bad, 12)
  if live then ui.drawCircleFilled(dot, 7, rgbm(0.2, 0.82, 0.38, 0.22), 12) end

  ui.dummy(vec2(width, h - 18))
end

--- Le pagine, numerate come i tasti funzione di un terminale.
---
--- Si cambiano col volante, ma anche col mouse: sopra ogni riquadro disegnato c'e un pulsante
--- invisibile della stessa misura. Il disegno resta nostro, il clic lo gestisce l'interfaccia -
--- cosi non si deve scegliere fra avere l'aspetto giusto e poterci cliccare sopra.
local function chromeNav(width)
  local count = #PAGES
  local gap = 2
  local w = (width - gap * (count - 1)) / count
  local h = 26
  local base = ui.getCursor()

  for i, name in ipairs(PAGES) do
    local x = base + vec2((i - 1) * (w + gap), 0)
    local selected = page == i

    ui.setCursor(x)
    local clicked = ui.invisibleButton('pitwall_nav_' .. i, vec2(w, h))
    local hovered = ui.itemHovered()

    local fill = selected and C.accent or (hovered and C.header or C.panel)
    local border = selected and C.accent or (hovered and C.edge or C.line)

    ui.drawRectFilled(x, x + vec2(w, h), fill)
    ui.drawRect(x, x + vec2(w, h), border, 0, 0, 1)

    ui.pushFont(ui.Font.Small)
    local label = string.format('%d %s', i, name)
    local lw = ui.measureText(label).x
    ui.drawText(label, x + vec2(math.max(4, (w - lw) / 2), 7),
                selected and rgbm(1, 1, 1, 1) or (hovered and C.text or C.muted))
    ui.popFont()

    if clicked then goToPage(i) end
  end

  ui.setCursor(base)
  ui.dummy(vec2(width, h + 6))
end

--- La riga in fondo: cosa fanno i pulsanti del volante. Su un computer che si usa senza mouse
--- deve esserci scritto, o si resta fermi a provare.
local function chromeBottom(width)
  local cursor = ui.getCursor()
  local h = 22

  ui.drawLine(cursor, cursor + vec2(width, 0), C.edge, 1)
  ui.drawRectFilled(cursor + vec2(0, 1), cursor + vec2(width, h), C.header)

  ui.dummy(vec2(0, 4))
  ui.sameLine(10)
  ui.pushFont(ui.Font.Small)

  local prev = COMMANDS[2] and COMMANDS[2].button
  local next_ = COMMANDS[1] and COMMANDS[1].button
  local hint = string.format('PAGINA  %s / %s',
    prev and prev:boundTo() or '--', next_ and next_:boundTo() or '--')
  ui.textColored(hint, C.label)

  ui.sameLine()
  local right = string.format('%s  %d/%d', PAGES[page], page, #PAGES)
  local rw = ui.measureText(right).x
  ui.sameLine(math.max(0, width - rw - 10))
  ui.textColored(right, C.muted)
  ui.popFont()

  ui.dummy(vec2(width, h - 12))
end

function windowGarage(dt)
  -- La lettura parte anche da qui: in alcune configurazioni l'aggiornamento di sistema non
  -- viene chiamato, e la finestra invece gira di sicuro, perche la stai guardando.
  safeTick(dt or 0.016, true)
  pcall(pollControls)
  pcall(applyUiCommand)

  -- Le impostazioni restano raggiungibili sempre: i pulsanti si assegnano anche a gioco fermo
  -- o senza SimHub acceso, ed e anzi il momento in cui ha piu senso farlo.
  if page == #PAGES then
    local w = ui.availableSpaceX()
    chromeTop(w)
    chromeNav(w)
    local ok, err = pcall(pageSettings)
    if not ok then ui.textColored(T('ac.errore') .. tostring(err), C.bad) end
    return
  end

  if not connected() then
    local w = ui.availableSpaceX()
    chromeTop(w)
    chromeNav(w)
    ui.dummy(vec2(1, 8))
    ui.textColored(T('ac.pitwall.non.collegato.2'), C.bad)
    ui.text(T('ac.serve.simhub.aperto.con.il.2'))

    ui.dummy(vec2(1, 10))
    ui.textColored(T('ac.diagnostica'), C.muted)
    ui.pushTextWrapPosition(0)

    ui.textColored(string.format(T('ac.cicli.sistema.finestra.2'), diag.ticks, diag.windowTicks), C.muted)
    ui.textColored(T('ac.accesso.ai.file') .. tostring(diag.io), diag.io == false and C.bad or C.muted)
    ui.textColored(T('ac.cartelle.di.sistema') .. tostring(diag.folders), diag.folders == false and C.bad or C.muted)

    if diag.lastError then ui.textColored(T('ac.errore.2') .. diag.lastError, C.bad) end
    if diag.parseError then ui.textColored(T('ac.lettura.json') .. diag.parseError, C.bad) end

    ui.dummy(vec2(1, 6))
    ui.textColored(T('ac.cartelle.risolte'), C.muted)
    local any = false
    for label, value in pairs(diag.folderValues) do
      any = true
      ui.textColored('    ' .. label .. ' = ' .. value, C.muted)
    end
    if not any then ui.textColored(T('ac.nessuna.richiesta'), C.muted) end

    ui.dummy(vec2(1, 6))
    ui.textColored(T('ac.file.cercati.senza.successo'), C.muted)
    for _, p in ipairs(diag.attempts) do
      ui.textColored('    ' .. p, C.muted)
    end
    if #diag.attempts == 0 then
      ui.textColored(T('ac.nessun.percorso.risolto'), C.muted)
    end

    ui.popTextWrapPosition()
    return
  end

  local width = ui.availableSpaceX()
  chromeTop(width)
  chromeNav(width)

  if not inGarage() then
    -- Fuori dai box il monitor non si spegne: resta in attesa, come quelli veri che continuano
    -- a mostrare la sessione finche qualcuno non rientra.
    ui.dummy(vec2(1, 40))
    ui.pushFont(ui.Font.Title)
    ui.textColored(T('ac.in.attesa'), C.screen)
    ui.popFont()
    ui.dummy(vec2(1, 8))
    ui.textColored(T('ac.il.computer.di.garage.si'), C.muted)
    ui.dummy(vec2(1, 20))
    ui.pushFont(ui.Font.Monospace)
    ui.textColored('   ' .. tostring(data.RaceControlText or ''), C.text)
    ui.popFont()
    return
  end

  -- Il contenuto sta dentro una cornice, cosi la riga di stato resta incollata in fondo invece
  -- di galleggiare dopo l'ultima riga scritta dalla pagina.
  local bottom = 30
  ui.childWindow('pitwall_body', vec2(width, math.max(60, ui.availableSpaceY() - bottom)), false, function()
    local ok, err = pcall(function()
      if page == 1 then pageWeather()
      elseif page == 2 then pageStrategy()
      elseif page == 3 then pageTiming()
      elseif page == 4 then pageCar()
      elseif page == 5 then pageTelemetry()
      elseif page == 6 then pageAnalysis()
      elseif page == 7 then pageHud()
      else pageSettings() end
    end)
    if not ok then ui.textColored(T('ac.errore.nella.pagina') .. tostring(err), C.bad) end
  end)

  chromeBottom(width)
end
