--[[
  Pitwall - controller WeatherFX.

  Questo e lo script che fa cambiare davvero il tempo durante la sessione.

  Il piano meteo nasce in Pitwall: il generatore lo costruisce dal clima del circuito, dal mese
  e dal seme del weekend, e lo salva come una serie di punti — al minuto 0 coperto, al 18
  pioggia leggera, al 24 pioggia forte, al 40 in asciugamento. Il plugin SimHub lo pubblica
  insieme al resto dello stato, e qui lo si esegue: a ogni fotogramma si trova dove siamo nella
  serie, si interpolano tipo di cielo, pioggia, temperature e vento, e si passa il risultato a
  Custom Shaders Patch.

  Il passaggio fra due punti non e uno scatto: `transition` va da 0 a 1 con una curva morbida,
  cosi il cielo si chiude piano e la pioggia arriva come arriva davvero.

  Se Pitwall non sta girando, il controller non combina guai: tiene le condizioni scelte in
  Quick Drive e basta.
]]

-- Due bandierine, piantate subito e a fine inizializzazione. Servono a distinguere due casi che
-- da fuori si assomigliano: "CSP non avvia questo controller" e "lo avvia ma muore caricandosi".
-- Senza, l'assenza di segnali non dice quale dei due.
ac.store('.pitwall.wfx.loaded', 1)

local weatherUtils = require('shared/sim/weather')

local cfg = ac.INIConfig.scriptSettings():mapSection('SETTINGS', {
  -- Quanto spesso rileggere il piano dal disco, in secondi.
  REFRESH_SECONDS = 2,
  -- Velocita con cui le pozzanghere si formano e si asciugano, 1 e il valore naturale.
  WATER_RATE = 1.0,
  -- Se Pitwall non risponde, dopo quanti secondi tornare alle condizioni di Quick Drive.
  FALLBACK_AFTER = 15,
})

--- Le condizioni scelte in Quick Drive, applicate pari pari.
---
--- Senza un piano di Pitwall questo controller deve comportarsi come quello predefinito di
--- Custom Shaders Patch: prendere il tempo selezionato e metterlo in pista, senza inventare
--- niente. Vale al primo fotogramma, quando il piano non e ancora arrivato, e in tutte le
--- sessioni che con Pitwall non c'entrano.
local conditions = ac.ConditionsSet()

local function applySelected()
  local selected = ac.getInputWeatherType()
  conditions.currentType = selected
  conditions.upcomingType = selected
  conditions.transition = 0
  conditions.temperatures = ac.getInputTemperatures()
  conditions.wind = ac.getInputWind()
  conditions.trackState = ac.getInputTrackState()
  conditions.humidity = 0.5
  conditions.pressure = 101325
  weatherUtils.setRainIntensity(conditions, true)
  weatherUtils.setRoadTemperature(conditions)
  ac.setConditionsSet2(conditions)
end

applySelected()

ac.store('.pitwall.wfx.init', 1)

local timeline = nil           -- serie di punti del piano
local planMinutes = 0          -- durata prevista della sessione
local sessionMinute = 0        -- a che punto siamo
local sinceRefresh = 99
local sinceData = 99
local statePath = nil
local water = 0                -- pozzanghere, si muovono piano
local wetness = 0              -- bagnato immediato, si muove in fretta
local source = 'nessuna'   -- da dove arriva il piano: app, disco o niente

-- ---------------------------------------------------------------------------------------------
-- Lettura del piano
-- ---------------------------------------------------------------------------------------------

--- Le cartelle che ac.getFolder restituisce non sono per forza stringhe Lua: possono essere
--- oggetti che le rappresentano. Vanno convertite, altrimenti il confronto con '' passa e la
--- concatenazione fallisce. Si prova piu di un posto perche il plugin pubblica lo stato in
--- due cartelle, e basta che una sia leggibile.
local function candidates()
  local list = {}

  local function add(id, suffix)
    if id == nil then return end
    local ok, base = pcall(ac.getFolder, id)
    if not ok or base == nil then return end
    local text = tostring(base)
    if text ~= '' then list[#list + 1] = text .. suffix end
  end

  add(ac.FolderID.Documents, '\\Pitwall\\live\\state.json')
  add(ac.FolderID.ACDocuments, '\\Pitwall\\state.json')
  return list
end

--- Il piano passato dall'app in pista attraverso lo spazio condiviso fra script.
---
--- E la strada buona: l'app legge il disco, cosa che sa fare di sicuro, e lascia qui il piano
--- gia pronto. Il controller non deve aprire niente, e funziona anche se in questo contesto il
--- filesystem non gli e concesso - cosa che da fuori non si vede, perche fallisce in silenzio.
local function readShared()
  local raw = ac.load('.pitwall.wx')
  if type(raw) ~= 'string' or raw == '' then return false end

  local points = {}
  for chunk in string.gmatch(raw, '([^;]+)') do
    local f = {}
    for piece in string.gmatch(chunk, '([^|]+)') do f[#f + 1] = piece end
    if #f >= 8 then
      points[#points + 1] = {
        Minute = tonumber(f[1]) or 0,
        Rain = tonumber(f[2]) or 0,
        Type = tonumber(f[3]) or 15,
        AirTemperature = tonumber(f[4]) or 22,
        RoadTemperature = tonumber(f[5]) or 32,
        Wetness = tonumber(f[6]) or 0,
        WindKmh = tonumber(f[7]) or 5,
        WindDirection = tonumber(f[8]) or 0
      }
    end
  end
  if #points == 0 then return false end

  timeline = points
  -- Il minuto arriva gia in minuti. Qui c'era una divisione per sessanta che rimediava a un
  -- difetto di chi lo pubblicava: ora il difetto e corretto alla fonte, e dividere ancora
  -- vorrebbe dire un piano meteo che avanza sessanta volte troppo piano.
  local minute = tonumber(ac.load('.pitwall.wx.minute'))
  if minute and minute >= 0 then sessionMinute = minute end
  sinceData = 0
  source = 'app'
  return true
end

local function refresh(dt)
  sinceRefresh = sinceRefresh + dt
  sinceData = sinceData + dt
  if sinceRefresh < cfg.REFRESH_SECONDS then return end
  sinceRefresh = 0

  if readShared() then return end

  -- Un percorso che ha gia funzionato si riusa; se smette, si ricomincia a cercare.
  local raw = nil
  if statePath then
    raw = io.load(statePath, nil)
    if raw == nil or raw == '' then statePath = nil; raw = nil end
  end

  if raw == nil then
    for _, candidate in ipairs(candidates()) do
      local attempt = io.load(candidate, nil)
      if attempt ~= nil and attempt ~= '' then
        statePath = candidate
        raw = attempt
        break
      end
    end
  end

  if raw == nil then return end

  -- Tre byte invisibili davanti alla graffa e JSON.parse torna a mani vuote senza dire niente.
  if raw:byte(1) == 239 and raw:byte(2) == 187 and raw:byte(3) == 191 then raw = raw:sub(4) end

  local ok, parsed = pcall(JSON.parse, raw)
  if not ok or type(parsed) ~= 'table' then return end

  local points = parsed.WeatherTimeline
  if type(points) ~= 'table' or #points == 0 then return end

  timeline = points
  source = 'disco'
  planMinutes = tonumber(parsed.SessionLengthMinutes) or 0

  -- Il minuto lo detta il plugin, che sa quanto e durata davvero la sessione comprese le pause.
  local elapsed = tonumber(parsed.SessionElapsedSeconds)
  if elapsed and elapsed >= 0 then sessionMinute = elapsed / 60 end

  sinceData = 0
end

-- ---------------------------------------------------------------------------------------------
-- Applicazione
-- ---------------------------------------------------------------------------------------------

local function lerp(a, b, t) return a + (b - a) * t end

--- Trova i due punti che circondano il minuto corrente e quanto siamo avanti fra i due.
local function bracket(minute)
  local previous = timeline[1]
  local next = timeline[1]

  for i = 1, #timeline do
    local p = timeline[i]
    if (tonumber(p.Minute) or 0) > minute then next = p break end
    previous = p
    next = timeline[math.min(i + 1, #timeline)]
  end

  local from = tonumber(previous.Minute) or 0
  local to = tonumber(next.Minute) or from
  local span = to - from
  local t = span > 0.01 and math.saturate((minute - from) / span) or 1

  return previous, next, t
end

local function apply(dt)
  -- Niente piano, o piano vecchio: si torna al tempo scelto in Quick Drive invece di restare
  -- aggrappati all'ultima cosa applicata. E quello che rende questo controller usabile anche
  -- fuori da Pitwall, in una sessione qualsiasi.
  if timeline == nil or #timeline == 0 or sinceData > cfg.FALLBACK_AFTER then
    applySelected()
    return
  end

  local previous, next, t = bracket(sessionMinute)
  local eased = math.smoothstep(t)

  conditions.currentType = math.floor(tonumber(previous.Type) or 15)
  conditions.upcomingType = math.floor(tonumber(next.Type) or 15)
  conditions.transition = eased

  local air = lerp(tonumber(previous.AirTemperature) or 22, tonumber(next.AirTemperature) or 22, eased)
  local road = lerp(tonumber(previous.RoadTemperature) or 32, tonumber(next.RoadTemperature) or 32, eased)
  conditions.temperatures.ambient = air
  conditions.temperatures.road = road

  local windSpeed = lerp(tonumber(previous.WindKmh) or 5, tonumber(next.WindKmh) or 5, eased)
  conditions.wind.direction = lerp(tonumber(previous.WindDirection) or 0, tonumber(next.WindDirection) or 0, eased)
  conditions.wind.speedFrom = windSpeed * 0.8
  conditions.wind.speedTo = windSpeed * 1.2

  -- La pioggia: intensita diretta dal piano, bagnato che la insegue in fretta, pozzanghere che
  -- si formano e si asciugano piano. E la differenza fra una pista che si bagna e una che resta
  -- allagata mezz'ora dopo che ha smesso.
  local rain = lerp(tonumber(previous.Rain) or 0, tonumber(next.Rain) or 0, eased)
  conditions.rainIntensity = rain

  local targetWetness = rain > 0.01 and math.min(1, rain * 3) or 0
  wetness = wetness + (targetWetness - wetness) * math.min(1, dt * 0.5)
  conditions.rainWetness = wetness

  local targetWater = lerp(tonumber(previous.Wetness) or 0, tonumber(next.Wetness) or 0, eased)
  water = water + (targetWater - water) * math.min(1, dt * 0.05 * cfg.WATER_RATE)
  conditions.rainWater = water

  conditions.humidity = math.saturate(0.35 + rain * 0.6 + (conditions.currentType >= 17 and 0.1 or 0))
  conditions.pressure = 101325 - rain * 1200

  ac.setConditionsSet2(conditions)
end

--- Battito per le altre app di Pitwall.
---
--- La app di garage non puo sapere da sola se il controller sta girando: vedeva il piano dire
--- pioggia e la pista asciutta, e doveva indovinare il perche. Queste tre righe le dicono i
--- fatti - se il controller e vivo, se sta ricevendo il piano, e che pioggia sta applicando -
--- cosi l'avviso dice cosa non va invece di accusare sempre la stessa cosa.
local beat = 0

--- Rapporto su file, per poter capire da fuori cosa sta succedendo.
---
--- Dentro al gioco non si vede niente: se il cielo resta sereno non si sa se il controller non
--- gira, se non trova il piano, o se il piano dice sereno. Due righe scritte su disco ogni paio
--- di secondi rispondono senza dover indovinare. Se questo file non compare proprio, vuol dire
--- che Custom Shaders Patch sta usando un altro controller.
local statusTimer = 99

--- Da quanto non si pubblicano i valori condivisi, in secondi.
local sinceStore = 0
local statusPath = nil

local function reportStatus(dt, ok, err)
  statusTimer = statusTimer + dt
  if statusTimer < 2 then return end
  statusTimer = 0

  if statusPath == nil then
    -- Due cartelle, perche se il rapporto non comparisse non si saprebbe se il controller non
    -- gira o se solo non riesce a scrivere li.
    local targets = {
      { ac.FolderID.Documents, '\\Pitwall\\live\\wfx_status.txt' },
      { ac.FolderID.ACDocuments, '\\Pitwall\\wfx_status.txt' }
    }
    for _, t in ipairs(targets) do
      local good, base = pcall(ac.getFolder, t[1])
      if good and base ~= nil and tostring(base) ~= '' then
        local candidate = tostring(base) .. t[2]
        local saved = false
        if pcall(function() saved = io.save(candidate, 'controller=pitwall\n') end) and saved ~= false then
          statusPath = candidate
          break
        end
      end
    end
    if statusPath == nil then return end
  end

  local lines = {
    'controller=pitwall',
    'beat=' .. beat,
    'punti=' .. (timeline and #timeline or 0),
    'minuto=' .. string.format('%.2f', sessionMinute),
    'da_ultimo_dato=' .. string.format('%.1f', sinceData),
    'pioggia_applicata=' .. string.format('%.3f', conditions.rainIntensity or 0),
    'bagnato_applicato=' .. string.format('%.3f', conditions.rainWetness or 0),
    'tipo_cielo=' .. tostring(conditions.currentType),
    'percorso=' .. tostring(statePath or 'nessuno'),
    'errore=' .. (ok and '-' or tostring(err))
  }
  pcall(function() io.save(statusPath, table.concat(lines, '\n') .. '\n') end)
end

function script.update(dt)
  refresh(dt)

  -- Fra una lettura e l'altra il tempo continua a scorrere: senza questo, il meteo avanzerebbe
  -- a scatti di due secondi invece che con continuita.
  if timeline ~= nil then sessionMinute = sessionMinute + dt / 60 end

  local ok, err = pcall(apply, dt)
  if not ok then ac.debug('pitwall.weather.error', tostring(err)) end

  beat = beat + 1
  reportStatus(dt, ok, err)

  -- Questi valori si pubblicano quattro volte al secondo, non a ogni fotogramma.
  --
  -- Qui c erano cinque ac.store e due ac.debug eseguiti a ogni frame. A cento fotogrammi al
  -- secondo fanno settecento chiamate con chiave stringa ogni secondo, dentro il processo di
  -- Assetto Corsa, e ac.debug in particolare e documentato come strumento di sviluppo: tiene
  -- una tabella per la finestra di debug di CSP e non e pensato per restare acceso.
  --
  -- Pesa in ogni sessione, non solo durante un campionato, perche questo controller e quello
  -- selezionato in weather_fx.ini e viene caricato sempre. Nessuno di questi valori serve a
  -- quella frequenza: chi li legge - la diagnostica dell app in pista - li guarda ogni tre
  -- secondi.
  --
  -- Il contatore beat continua a salire a ogni fotogramma: e la prova che il controller gira,
  -- e perderebbe senso rallentandolo. Cambia solo ogni quanto lo si scrive fuori.
  sinceStore = sinceStore + dt
  if sinceStore >= 0.25 then
    sinceStore = 0
    ac.store('.pitwall.wfx.beat', beat)
    ac.store('.pitwall.wfx.plan', (timeline ~= nil and sinceData <= cfg.FALLBACK_AFTER) and 1 or 0)
    ac.store('.pitwall.wfx.rain', conditions.rainIntensity or 0)
    ac.store('.pitwall.wfx.source', source)
    ac.store('.pitwall.wfx.points', timeline and #timeline or 0)
    if not ok then ac.store('.pitwall.wfx.error', tostring(err)) end
  end
end
