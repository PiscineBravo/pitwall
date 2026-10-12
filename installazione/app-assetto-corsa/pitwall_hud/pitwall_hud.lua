--[[

  Pitwall - cruscotto di gara.

  Cinque pannelli separati invece di una finestra sola, perche ognuno vuole stare in un posto
  diverso dello schermo e ognuno si guarda in un momento diverso: la classifica di sfuggita, il
  delta mentre si spinge, la mappa quando si cerca il traffico, l'ingegnere quando si decide.
  Finestre separate vogliono anche dire che ciascuno si sposta e si ridimensiona per conto suo,
  che e come funzionano i cruscotti veri.

  I dati arrivano da due parti, e la divisione non e casuale:
  - quello che riguarda la gara - classifica, bandiere, qualifiche, strategia - lo pubblica il
    plugin SimHub in race_control.txt, perche nasce dal campionato e il gioco non lo sa;
  - quello che riguarda la vettura - delta, ERS, DRS, carburante, posizione in pista - si legge
    da Custom Shaders Patch, che ce l'ha di prima mano e senza ritardo.
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


local REFRESH = 0.25

local C = {
  bg       = rgbm(0.04, 0.05, 0.06, 0.82),
  panel    = rgbm(0.07, 0.09, 0.11, 0.9),
  line     = rgbm(0.18, 0.22, 0.27, 1),
  text     = rgbm(0.92, 0.95, 0.98, 1),
  muted    = rgbm(0.50, 0.56, 0.64, 1),
  label    = rgbm(0.38, 0.44, 0.52, 1),
  accent   = rgbm(0.90, 0.08, 0.08, 1),
  player   = rgbm(1.00, 0.82, 0.15, 1),
  good     = rgbm(0.20, 0.85, 0.40, 1),
  warn     = rgbm(1.00, 0.72, 0.10, 1),
  bad      = rgbm(0.95, 0.25, 0.22, 1),
  purple   = rgbm(0.72, 0.35, 0.95, 1),
  ers      = rgbm(0.25, 0.80, 1.00, 1),
  drs      = rgbm(0.20, 0.95, 0.45, 1)
}

-- ---------------------------------------------------------------------------------------------
-- Lettura dello stato pubblicato dal plugin
-- ---------------------------------------------------------------------------------------------

local state = {
  status = 'Inactive', text = '-', lap = '0/0', position = 0,
  seq = -1, qSegment = '', qLeft = 0, qSurviving = 0,
  message = '', tyre = '', nextTyre = '', fPhase = 'Idle', lights = 0, pitStart = false
}
local standings = {}
local strategies = {}
local connected = false
local statePath = nil
local sinceRead = 99
local sinceData = 99
local lastSeq = -1

local function split(text, sep)
  local out = {}
  for piece in string.gmatch(text .. sep, '([^' .. sep .. ']*)' .. sep) do out[#out + 1] = piece end
  return out
end

--- Dove cercare il file. Quello che torna da ac.getFolder non e per forza una stringa Lua:
--- va convertito, o il confronto fallisce in silenzio.
local function candidates()
  local list = {}
  local function add(id, suffix)
    if id == nil then return end
    local ok, base = pcall(ac.getFolder, id)
    if not ok or base == nil then return end
    local text = tostring(base)
    if text ~= '' then list[#list + 1] = text .. suffix end
  end
  add(ac.FolderID.ACDocuments, '\\Pitwall\\race_control.txt')
  add(ac.FolderID.Documents, '\\Pitwall\\live\\race_control.txt')
  return list
end

local function parse(text)
  local rows, options = {}, {}

  for line in string.gmatch(text, '([^\n]+)') do
    local key, value = line:match('^(%w+)=(.*)$')
    if key == 'status' then state.status = value
    elseif key == 'text' then state.text = value
    elseif key == 'lap' then state.lap = value
    elseif key == 'lingua' then inInglese = value == 'en'
    elseif key == 'position' then state.position = tonumber(value) or 0
    elseif key == 'message' then state.message = value
    elseif key == 'seq' then state.seq = tonumber(value) or 0
    elseif key == 'fphase' then state.fPhase = value
    elseif key == 'lights' then state.lights = tonumber(value) or 0
    elseif key == 'pitstart' then state.pitStart = value == '1'
    elseif key == 'qseg' then state.qSegment = value
    elseif key == 'qleft' then state.qLeft = tonumber(value) or 0
    elseif key == 'qsurv' then state.qSurviving = tonumber(value) or 0
    elseif key == 'tyre' then state.tyre = value
    elseif key == 'nexttyre' then state.nextTyre = value
    elseif key == 'pos' then
      local f = split(value, '|')
      rows[#rows + 1] = {
        position = tonumber(f[1]) or 0, name = f[2] or '', gap = f[3] or '',
        compound = f[4] or '', age = tonumber(f[5]) or 0,
        player = f[6] == '1', pit = f[7] == '1',
        eliminated = f[8] == '1', dropZone = f[8] == '2'
      }
    elseif key == 'str' then
      local f = split(value, '|')
      options[#options + 1] = {
        id = f[1] or '', name = f[2] or '', summary = f[3] or '',
        delta = tonumber(f[4]) or 0, position = tonumber(f[5]) or 0,
        risk = tonumber(f[6]) or 0, recommended = f[7] == '1', selected = f[8] == '1'
      }
    end
  end

  standings = rows
  strategies = options
end

local function refresh(dt)
  sinceRead = sinceRead + dt
  sinceData = sinceData + dt
  if sinceRead < REFRESH then return end
  sinceRead = 0

  local raw = nil
  if statePath then
    raw = io.load(statePath, nil)
    if raw == nil or raw == '' then statePath = nil end
  end
  if raw == nil or raw == '' then
    for _, path in ipairs(candidates()) do
      local attempt = io.load(path, nil)
      if attempt ~= nil and attempt ~= '' then statePath = path; raw = attempt; break end
    end
  end
  if raw == nil or raw == '' then
    connected = sinceData < 5
    return
  end

  pcall(parse, raw)

  -- Il file letto non vuol dire dati vivi: se il plugin si ferma il file resta dov'e. Conta
  -- solo che il contatore di pubblicazione sia salito.
  if state.seq ~= lastSeq then
    lastSeq = state.seq
    sinceData = 0
  end
  connected = sinceData < 5
end

--- Manda un comando al plugin, lo stesso canale dei pulsanti del volante.
local function sendCommand(line)
  local ok, base = pcall(ac.getFolder, ac.FolderID.ACDocuments)
  if not ok or base == nil then return end
  pcall(function() io.save(tostring(base) .. '\\Pitwall\\command.txt', line .. '\n') end)
end

-- ---------------------------------------------------------------------------------------------
-- Quali pannelli tenere accesi
--
-- La scelta si fa dal computer di garage, che e un'altra app e quindi non puo aprire e chiudere
-- queste finestre direttamente: passa dallo spazio condiviso fra script. Qui si legge cosa ha
-- chiesto e si esegue.
--
-- La scelta va anche ricordata: ac.storage sopravvive alla chiusura del gioco, lo spazio
-- condiviso no. Cosi i pannelli si ritrovano come li avevi lasciati invece di tornare tutti
-- accesi ogni volta.
-- ---------------------------------------------------------------------------------------------

local PANELS = { 'standings', 'map', 'delta', 'ers', 'pit' }

local saved = ac.storage({
  standings = true, map = true, delta = true, ers = true, pit = true
}, 'pitwall_hud_')

local applied = {}
local panelTimer = 0

local function followPanels(dt)
  panelTimer = panelTimer + dt
  if panelTimer < 0.4 then return end
  panelTimer = 0

  for _, id in ipairs(PANELS) do
    -- Se il garage ha espresso una scelta la si prende e la si ricorda; altrimenti vale
    -- l'ultima ricordata.
    local asked = ac.load('.pitwall.hud.' .. id)
    local want
    if asked == nil then
      want = saved[id]
      -- Si rimanda indietro lo stato, cosi il garage mostra le caselle giuste appena si apre.
      ac.store('.pitwall.hud.' .. id, want and 1 or 0)
    else
      want = tonumber(asked) == 1
      saved[id] = want
    end

    if applied[id] ~= want then
      applied[id] = want
      pcall(function() ac.setWindowOpen(id, want) end)
    end
  end
end

function script.update(dt)
  pcall(refresh, dt)
  pcall(followPanels, dt)
end

-- ---------------------------------------------------------------------------------------------
-- Pezzi comuni
-- ---------------------------------------------------------------------------------------------

local function panel(width, height)
  local cursor = ui.getCursor()
  ui.drawRectFilled(cursor, cursor + vec2(width, height), C.panel)
  ui.drawRect(cursor, cursor + vec2(width, height), C.line, 0, 0, 1)
  return cursor
end

local function title(text, width, colour)
  local cursor = ui.getCursor()
  ui.drawRectFilled(cursor, cursor + vec2(width, 20), rgbm(0.09, 0.11, 0.14, 1))
  ui.drawRectFilled(cursor, cursor + vec2(3, 20), colour or C.accent)
  ui.dummy(vec2(0, 3))
  ui.sameLine(10)
  ui.pushFont(ui.Font.Small)
  ui.textColored(text, C.muted)
  ui.popFont()
  ui.dummy(vec2(width, 8))
end

local function offline()
  ui.pushFont(ui.Font.Small)
  ui.textColored(T('ac.pitwall.non.collegato'), C.label)
  ui.popFont()
end

-- ---------------------------------------------------------------------------------------------
-- Classifica
-- ---------------------------------------------------------------------------------------------

function windowStandings(dt)
  local width = ui.availableSpaceX()

  if state.qSegment ~= '' then
    -- In qualifica il titolo dice la manche e quanto manca: e la prima cosa da sapere.
    local left = math.max(0, state.qLeft)
    title(string.format('%s   %d:%02d   passano in %d',
          state.qSegment, math.floor(left / 60), left % 60, state.qSurviving), width, C.bad)
  else
    title('CLASSIFICA   giro ' .. state.lap, width)
  end

  if state.pitStart then
    ui.pushFont(ui.Font.Title)
    ui.textColored(T('ac.partenza.dai.box'), C.warn)
    ui.popFont()
    ui.pushTextWrapPosition(0)
    ui.textColored(T('ac.posizione.in.griglia.non.presa'), C.muted)
    ui.popTextWrapPosition()
    ui.dummy(vec2(1, 6))
  end

  if not connected then offline() return end
  if #standings == 0 then
    ui.pushFont(ui.Font.Small)
    ui.textColored(T('ac.in.attesa.di.dati'), C.label)
    ui.popFont()
    return
  end

  for i = 1, #standings do
    local r = standings[i]
    local colour = r.player and C.player or C.text

    -- La riga rossa delle qualifiche: chi e gia fuori resta spento, chi ci finirebbe adesso e
    -- acceso. Si guarda piu di quella dei tempi.
    if r.eliminated then colour = rgbm(0.40, 0.42, 0.46, 1)
    elseif r.dropZone then colour = C.bad end

    if r.player then
      local cursor = ui.getCursor()
      ui.drawRectFilled(cursor - vec2(4, 1), cursor + vec2(width, 16), rgbm(0.18, 0.14, 0.03, 1))
    end

    ui.pushFont(ui.Font.Monospace)
    ui.textColored(string.format('%2d', r.position), r.player and C.player or C.label)
    ui.sameLine(30)
    ui.textColored(r.name ~= '' and r.name or '---', colour)
    ui.sameLine(84)
    ui.textColored(r.gap ~= '' and r.gap or '-', r.player and C.player or C.muted)
    ui.sameLine(150)
    if r.pit then
      ui.textColored(T('ac.box'), C.warn)
    else
      ui.textColored(r.compound ~= '' and r.compound or '-', colour)
      ui.sameLine(178)
      ui.textColored(tostring(r.age), r.age > 25 and C.warn or C.label)
    end
    ui.popFont()
  end
end

-- ---------------------------------------------------------------------------------------------
-- Mappa del circuito
--
-- Il tracciato non si disegna da un'immagine: si campiona la linea della pista da zero a uno e
-- si proiettano i punti su due dimensioni. Viene fuori la forma vera del circuito, qualunque
-- esso sia, senza doverne preparare una mappa per ognuno.
-- ---------------------------------------------------------------------------------------------

local trackShape = nil      -- punti normalizzati 0..1 della forma del tracciato
local trackReady = false

local function buildTrack()
  if trackReady then return end

  local points = {}
  local minX, maxX, minZ, maxZ = 1e9, -1e9, 1e9, -1e9

  for i = 0, 359 do
    local ok, p = pcall(ac.trackProgressToWorldCoordinate, i / 360)
    if not ok or p == nil then return end
    points[#points + 1] = { x = p.x, z = p.z }
    if p.x < minX then minX = p.x end
    if p.x > maxX then maxX = p.x end
    if p.z < minZ then minZ = p.z end
    if p.z > maxZ then maxZ = p.z end
  end

  local spanX = math.max(1, maxX - minX)
  local spanZ = math.max(1, maxZ - minZ)
  local span = math.max(spanX, spanZ)

  -- Si normalizza con lo stesso fattore sui due assi, o il circuito verrebbe schiacciato.
  for _, p in ipairs(points) do
    p.nx = (p.x - minX) / span + (1 - spanX / span) / 2
    p.nz = (p.z - minZ) / span + (1 - spanZ / span) / 2
  end

  trackShape = { points = points, minX = minX, minZ = minZ, span = span,
                 offX = (1 - spanX / span) / 2, offZ = (1 - spanZ / span) / 2 }
  trackReady = true
end

local function toMap(origin, size, worldX, worldZ)
  local t = trackShape
  local nx = (worldX - t.minX) / t.span + t.offX
  local nz = (worldZ - t.minZ) / t.span + t.offZ
  return origin + vec2(nx * size, nz * size)
end

function windowMap(dt)
  local width = ui.availableSpaceX()
  title('MAPPA', width)

  pcall(buildTrack)
  if not trackReady then
    ui.pushFont(ui.Font.Small)
    ui.textColored(T('ac.tracciato.non.disponibile'), C.label)
    ui.popFont()
    return
  end

  local size = math.min(width, ui.availableSpaceY() - 6)
  local origin = ui.getCursor() + vec2((width - size) / 2, 0)
  local pad = size * 0.06
  local inner = size - pad * 2
  local base = origin + vec2(pad, pad)

  -- Il tracciato, due volte: una linea spessa scura sotto e una chiara sopra, cosi si stacca
  -- dal fondo come su una grafica televisiva.
  local pts = trackShape.points
  for i = 1, #pts do
    local a = pts[i]
    local b = pts[i % #pts + 1]
    local pa = base + vec2(a.nx * inner, a.nz * inner)
    local pb = base + vec2(b.nx * inner, b.nz * inner)
    ui.drawLine(pa, pb, rgbm(0.10, 0.12, 0.15, 1), 7)
  end
  for i = 1, #pts do
    local a = pts[i]
    local b = pts[i % #pts + 1]
    local pa = base + vec2(a.nx * inner, a.nz * inner)
    local pb = base + vec2(b.nx * inner, b.nz * inner)
    ui.drawLine(pa, pb, rgbm(0.32, 0.38, 0.46, 1), 3)
  end

  -- Le vetture. La tua piu grande e gialla, le altre piccole; chi e ai box resta spento.
  local sim = ac.getSim()
  if sim == nil then return end

  for i = 0, (sim.carsCount or 1) - 1 do
    local car = ac.getCar(i)
    if car ~= nil and car.position ~= nil then
      local p = toMap(base, inner, car.position.x, car.position.z)
      local mine = i == 0
      local colour = mine and C.player or C.text
      if car.isInPitlane then colour = C.label end
      ui.drawCircleFilled(p, mine and 5 or 3.5, colour, 12)
      if mine then ui.drawCircle(p, 8, rgbm(1, 0.82, 0.15, 0.45), 14, 1.5) end
    end
  end

  ui.dummy(vec2(size, size))
end

-- ---------------------------------------------------------------------------------------------
-- Delta
-- ---------------------------------------------------------------------------------------------

function windowDelta(dt)
  local width = ui.availableSpaceX()
  local car = ac.getCar(0)
  if car == nil then title('DELTA', width) offline() return end

  local delta = car.performanceMeter or 0
  local ahead = delta < 0
  local colour = ahead and C.good or (delta > 0 and C.bad or C.muted)

  title('DELTA SUL GIRO', width, colour)

  -- La barra: il centro e il riferimento, a sinistra si guadagna, a destra si perde. Satura a
  -- un secondo, oltre il quale non serve sapere quanto: serve sapere che e tanto.
  local cursor = ui.getCursor()
  local h = 16
  ui.drawRectFilled(cursor, cursor + vec2(width, h), rgbm(0.10, 0.12, 0.15, 1))
  local middle = width / 2
  local fill = math.min(1, math.abs(delta)) * middle
  if delta < 0 then
    ui.drawRectFilled(cursor + vec2(middle - fill, 0), cursor + vec2(middle, h), C.good)
  else
    ui.drawRectFilled(cursor + vec2(middle, 0), cursor + vec2(middle + fill, h), C.bad)
  end
  ui.drawLine(cursor + vec2(middle, -2), cursor + vec2(middle, h + 2), C.text, 2)
  ui.dummy(vec2(width, h + 6))

  ui.pushFont(ui.Font.Huge)
  local text = string.format('%+.3f', delta)
  local w = ui.measureText(text).x
  ui.dummy(vec2(0, 0))
  ui.sameLine(math.max(0, (width - w) / 2))
  ui.textColored(text, colour)
  ui.popFont()

  ui.pushFont(ui.Font.Monospace)
  local best = car.bestLapTimeMs or 0
  local function lap(ms)
    if ms == nil or ms <= 0 then return '--:--.---' end
    return string.format('%d:%06.3f', math.floor(ms / 60000), (ms % 60000) / 1000)
  end
  ui.textColored(T('ac.migliore') .. lap(best), C.label)
  ui.sameLine(150)
  ui.textColored(T('ac.ultimo') .. lap(car.previousLapTimeMs), C.muted)
  ui.popFont()
end

-- ---------------------------------------------------------------------------------------------
-- ERS, DRS e carburante
-- ---------------------------------------------------------------------------------------------

local function bar(width, height, value, colour, back)
  local cursor = ui.getCursor()
  ui.drawRectFilled(cursor, cursor + vec2(width, height), back or rgbm(0.10, 0.12, 0.15, 1))
  ui.drawRectFilled(cursor, cursor + vec2(width * math.max(0, math.min(1, value)), height), colour)
  ui.dummy(vec2(width, height))
end

function windowErs(dt)
  local width = ui.availableSpaceX()
  title('ENERGIA', width, C.ers)

  local car = ac.getCar(0)
  if car == nil then offline() return end

  -- La carica della batteria, con quanti kilojoule restano se la vettura li espone.
  local charge = car.kersCharge or 0
  ui.pushFont(ui.Font.Small)
  ui.textColored(T('ac.ers'), C.label)
  ui.sameLine(44)
  ui.textColored(string.format('%.0f%%', charge * 100), charge < 0.15 and C.warn or C.ers)
  if (car.kersMaxKJ or 0) > 0 then
    ui.sameLine(100)
    ui.textColored(string.format(T('ac.kj'), car.kersCurrentKJ or 0, car.kersMaxKJ), C.label)
  end
  ui.popFont()
  bar(width, 12, charge, C.ers)

  -- Quanta ne sta mandando adesso: dice se stai scaricando o ricaricando.
  ui.dummy(vec2(1, 4))
  ui.pushFont(ui.Font.Small)
  local using = car.kersInput or 0
  ui.textColored(car.kersCharging and 'IN RICARICA' or (using > 0.01 and 'IN SCARICA' or 'FERMA'),
                 car.kersCharging and C.good or (using > 0.01 and C.ers or C.label))
  ui.popFont()
  bar(width, 6, using, C.ers)

  -- DRS e carburante, la riga che si guarda prima della staccata.
  ui.dummy(vec2(1, 6))
  ui.pushFont(ui.Font.Small)
  if car.drsPresent then
    ui.textColored(T('ac.drs'), C.label)
    ui.sameLine(44)
    ui.textColored(car.drsActive and 'APERTO' or (car.drsAvailable and 'DISPONIBILE' or 'chiuso'),
                   car.drsActive and C.drs or (car.drsAvailable and C.good or C.label))
  end

  ui.sameLine(150)
  local fuel = car.fuel or 0
  local perLap = car.fuelPerLap or 0
  ui.textColored(string.format(T('ac.l'), fuel), fuel < 5 and C.bad or C.text)
  if perLap > 0.05 then
    ui.sameLine(210)
    ui.textColored(string.format(T('ac.giri'), fuel / perLap), fuel / perLap < 2 and C.warn or C.muted)
  end
  ui.popFont()
end

-- ---------------------------------------------------------------------------------------------
-- Pannello dell'ingegnere
-- ---------------------------------------------------------------------------------------------

local RISK = { [1] = 'sicuro', [2] = 'da seguire', [3] = 'scommessa' }

function windowPit(dt)
  local width = ui.availableSpaceX()
  title('INGEGNERE DI PISTA', width)

  if not connected then offline() return end

  -- L'ultima comunicazione radio, per esteso: e la cosa che si perde se si guarda altrove.
  if state.message ~= '' then
    ui.pushTextWrapPosition(0)
    ui.textColored(state.message, C.text)
    ui.popTextWrapPosition()
  else
    ui.textColored(T('ac.nessuna.comunicazione'), C.label)
  end

  ui.dummy(vec2(1, 6))
  ui.pushFont(ui.Font.Small)
  ui.textColored(T('ac.gomme'), C.label)
  ui.sameLine(60)
  ui.textColored(state.tyre ~= '' and state.tyre or '-', C.text)
  if state.nextTyre ~= '' then
    ui.sameLine(130)
    ui.textColored(T('ac.prossime'), C.label)
    ui.sameLine(200)
    ui.textColored(state.nextTyre, C.good)
  end
  ui.popFont()

  ui.dummy(vec2(1, 6))
  ui.drawLine(ui.getCursor(), ui.getCursor() + vec2(width, 0), C.line, 1)
  ui.dummy(vec2(1, 6))

  if #strategies == 0 then
    ui.textColored(T('ac.nessuno.scenario.proposto'), C.label)
    return
  end

  -- Gli scenari, uno per riga, cliccabili. Quello consigliato ha la barretta d'accento, quello
  -- scelto resta acceso: si decide da qui senza aprire altro.
  for i = 1, #strategies do
    local o = strategies[i]
    local cursor = ui.getCursor()
    local h = 38

    local clicked = ui.invisibleButton('pitwall_str_' .. i, vec2(width, h))
    local hovered = ui.itemHovered()

    ui.drawRectFilled(cursor, cursor + vec2(width, h),
                      o.selected and rgbm(0.14, 0.18, 0.12, 1)
                      or (hovered and rgbm(0.11, 0.13, 0.16, 1) or C.panel))
    if o.recommended then
      ui.drawRectFilled(cursor, cursor + vec2(3, h), C.good)
    elseif o.selected then
      ui.drawRectFilled(cursor, cursor + vec2(3, h), C.player)
    end

    ui.pushFont(ui.Font.Small)
    ui.drawText(o.id .. '   ' .. o.name, cursor + vec2(12, 5), o.selected and C.player or C.text)
    ui.drawText(o.summary, cursor + vec2(12, 21), C.label)

    local right = string.format('%+.1fs   P%d   %s', o.delta, o.position, RISK[o.risk] or '-')
    local rw = ui.measureText(right).x
    ui.drawText(right, cursor + vec2(width - rw - 10, 13),
                o.delta <= 0 and C.good or C.muted)
    ui.popFont()

    ui.dummy(vec2(width, h + 3))

    if clicked then sendCommand('select-strategy ' .. o.id) end
  end
end
