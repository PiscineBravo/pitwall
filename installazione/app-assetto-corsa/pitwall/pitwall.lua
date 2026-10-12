--[[

  Pitwall - companion in-game per Assetto Corsa.

  Fa tre cose.

  1. Mostra in pista quello che la direzione gara ha deciso: safety car con le sue fasi, virtual
     safety car, giro di formazione, bandiere, delta da rispettare, penalita e radio
     dell'ingegnere. Questa parte funziona sempre.

  2. Esegue i comandi del direttore dell'intelligenza artificiale: livello e aggressivita giro
     per giro, rientri ai box alla fine di ogni stint con la mescola prevista, errori e ritiri.
     E quello che rende la strategia vera: senza, gli avversari non degradano e fermarsi ai box
     sarebbe solo tempo perso.

  3. Guida la vettura di sicurezza. Resta parcheggiata ai box per tutta la gara e scende in
     pista solo quando la direzione gara la manda, detta il ritmo, lascia sdoppiare i doppiati
     e poi rientra.

  I punti 2 e 3 richiedono che la pista consenta gli script di physics. Se non li consente
  l'app lo scrive e resta un overlay.

  Lo stato arriva dal plugin SimHub attraverso Documents/Pitwall/live/race_control.txt: un file
  di testo con una coppia chiave=valore per riga, piu una riga "ai=" per ogni vettura.
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


local REFRESH_INTERVAL = 0.35
local STALE_AFTER = 4.0

local state = {
  status = 'Inactive',
  text = '-',
  delta = 0,
  targetKmh = 0,
  pitClosed = false,
  yellow = {},
  message = '',
  penalties = 0,
  position = 0,
  lap = '0/0',
  scPhase = 'Idle',
  seq = -1,
  fPhase = 'Idle',
  autopilot = false,
  lights = 0,
  pitStart = false,
  pitStartDelay = 12,
  qSegment = '',
  qLeft = 0,
  qSurviving = 0,
  gridSlot = 0,
  gridNotice = '',
  giveBack = 0,
  giveBackLeft = 0,
  gridAlong = 2.5,
  gridAcross = 1.4,
  drsEnabled = false,
  drsNotice = '',
  drsGap = 0,
  drsPlayer = false,
  fuelRunMargin = 1.15,
  fuelRaceMargin = 1.07,
  fuelReserve = 2
}

local formationHeld = false   -- vero mentre le vetture sono bloccate in griglia
local formationAwaitingStart = false

-- Serpentine di riscaldamento.
--
-- Ampiezza in METRI. Esiste una seconda funzione, setAISplineAbsoluteOffset, che prende metri
-- invece del valore normalizzato: con quello normalizzato la stessa cifra dava uno spostamento
-- diverso su ogni circuito, perche veniva saturato dalla larghezza della pista.
local WEAVE_AMPLITUDE = 1.2

-- Velocita della serpentina in radianti al secondo: un ciclo completo ogni cinque secondi.
--
-- Era piu del doppio, un ciclo ogni due secondi e mezzo, e le vetture sbandavano. Spostarsi di
-- un metro e due da una parte e dall altra in poco piu di un secondo non e una serpentina: e
-- una correzione brusca, e l IA che cerca di seguirla perde la vettura. Con i contatti che
-- seguono, in mezzo a un gruppo compatto.
local WEAVE_RATE = 1.3

-- Oltre questa velocita non si zigzaga piu, in km/h: sotto la prima si serpeggia pieno, sopra
-- la seconda si va diritti, in mezzo si sfuma.
--
-- La ragione e fisica, non estetica. Un metro di scostamento laterale preso a 90 km/h e un
-- movimento morbido; lo stesso metro a 230 km/h chiede un angolo di sterzo che manda la vettura
-- in traverso. E anche quello che si vede davvero: in ricognizione si zigzaga nei tratti lenti
-- e si accelera diritti nei rettilinei, che e il momento in cui si scalda il motore e si prende
-- carico sulle gomme.
local WEAVE_FULL_KMH = 110
local WEAVE_NONE_KMH = 175
local weavePhase = 0

-- Quanto ci mette la serpentina ad arrivare a regime e a spegnersi, in secondi. Senza questa
-- rampa le vetture scatterebbero di lato nell'istante in cui comincia la ricognizione.
local WEAVE_FADE_SECONDS = 2.5

local weaveRamp = 0        -- da 0 a 1, quanto e in forza la serpentina adesso
local weaveApplied = {}    -- per quali vetture abbiamo scritto uno scostamento

-- ---------------------------------------------------------------------------------------------
-- Le distanze in ricognizione
--
-- Con tutti limitati alla stessa velocita le vetture si incollano: l'IA raggiunge chi ha davanti
-- e li resta, e il giro di formazione diventa un trenino attaccato. In Formula 1 invece il gruppo
-- e disteso, qualche lunghezza fra una vettura e l'altra.
--
-- Qui ognuna guarda quanto le manca a quella davanti e aggiusta la propria andatura: troppo
-- vicina rallenta, troppo lontana recupera. Si sistemano da sole in pochi secondi e restano
-- distanziate, senza che nessuno debba calcolare una griglia di velocita.
-- ---------------------------------------------------------------------------------------------

local FORMATION_GAP = 25          -- metri voluti fra una vettura e l'altra
local FORMATION_DEADBAND = 4      -- metri di tolleranza: sotto, non si corregge niente
local gapAhead = {}               -- indice vettura -> metri da chi la precede, lungo la pista
local speedAhead = {}             -- indice vettura -> km/h di chi la precede

-- Dichiarata qui e non piu sotto: la misura dei distacchi deve poterla vedere per saltare la
-- safety car, e in Lua una locale dichiarata dopo non esiste per chi viene prima.
local safetyCarIndex = nil

local function measureGaps()
  gapAhead = {}
  speedAhead = {}
  local sim = ac.getSim()
  if sim == nil then return end

  local length = sim.trackLengthM
  if length == nil or length <= 0 then return end

  -- In ordine di posizione: cosi "quella davanti" e davvero quella davanti.
  local order = {}
  for i = 0, (sim.carsCount or 1) - 1 do
    local car = ac.getCar(i)
    -- Si saltano la safety car, che in ricognizione sta ferma ai box, e chiunque sia in corsia:
    -- lasciarle nella catena farebbe credere a chi le segue di avere davanti il vuoto, e a chi
    -- le precede di avere qualcuno addosso.
    if car ~= nil and (car.racePosition or 0) > 0 and i ~= safetyCarIndex
       and car.isInPitlane ~= true then
      order[#order + 1] = {
        index = i,
        position = car.racePosition,
        spline = car.splinePosition or 0,
        speed = car.speedKmh or 0
      }
    end
  end
  table.sort(order, function(a, b) return a.position < b.position end)

  for n = 2, #order do
    local me, ahead = order[n], order[n - 1]

    -- Distanza lungo il tracciato, non in linea d'aria. In un tornante due vetture distanti
    -- quaranta metri di pista ne hanno dieci in linea retta, e chi sta dietro veniva rallentato
    -- in ogni curva lenta del giro senza che ce ne fosse motivo.
    local d = (ahead.spline - me.spline) % 1.0
    gapAhead[me.index] = d * length
    speedAhead[me.index] = ahead.speed
  end
end

--- L'andatura di una vettura in ricognizione.
---
--- Si insegue la velocita di chi precede, non un numero fisso: questo e il pezzo che mancava.
--- Prima la correzione guardava solo il distacco, e un anello posizione-velocita senza
--- smorzamento oscilla invece di stabilizzarsi - fisarmonica, con le vetture attaccate meta del
--- tempo e lentissime l'altra meta. Partendo dalla velocita di chi sta davanti si e gia alla
--- velocita giusta, e il distacco serve solo a correggere il resto.
--- Da quanto dura la ricognizione, in secondi. Serve alla partenza scaglionata.
local rollingTimer = 0

--- Un orologio che scorre sempre, per l'ondeggiamento dell'andatura.
---
--- Quello della ricognizione si azzera a ogni fase, e andava bene finche l'ondeggiamento
--- serviva solo li. Ma le gomme vanno scaldate anche nei giri di uscita in prove e qualifica,
--- e quelli non sono una fase della gara: serve un orologio che non si fermi mai.
local warmupClock = 0

-- Due comportamenti, uno acceso e uno spento.
--
-- L'andatura che respira e accesa: accelerare e rallentare e quello che scalda davvero le
-- gomme e i freni, cosa che una velocita costante non fa mai. A ritmo fisso la vettura resta
-- in seconda e arriva in griglia con le gomme fredde, che e il contrario di quello che serve.
--
-- La partenza scaglionata resta spenta: insieme all'altra aveva prodotto contatti all'arrivo
-- in griglia, quindi si riaccende una cosa alla volta e si guarda.
--
-- Sono quelli giusti - in Formula 1 si lascia la griglia scaglionati, e in ricognizione non si
-- tiene una velocita fissa ma si accelera e si frena per scaldare gomme e freni - e il codice
-- qui sotto li fa. Ma accesi insieme hanno prodotto contatti all arrivo in griglia: il gruppo
-- si allunga troppo, le vetture arrivano alla spicciolata e si tamponano mentre si schierano.
--
-- Restano a zero finche non li proviamo uno alla volta, con calma, misurando. Un valore di
-- partenza ragionevole per provarli: 0.6 lo scaglionamento, 0.20 il respiro - meta di quello
-- che avevo messo la prima volta.

--- Quanto aspetta ogni vettura prima di lasciare la griglia, in secondi per posizione.
local STAGGER_SECONDS = 0

--- Come respira l'andatura: una spinta lunga, un rilascio corto, e si ricomincia.
---
--- Un tetto fisso di velocita, qualunque sia il numero, lascia la vettura a giri costanti in un
--- rapporto basso: il motore non si scalda, i freni non lavorano, e nelle gomme non entra energia
--- perche non ci sono ne accelerazioni ne frenate. Quello che scalda e il **lavoro**, non la
--- velocita media.
---
--- Ma la prima versione di questa idea era un seno larghissimo, simmetrico, e peggiorava le cose
--- invece di migliorarle. Il conto, fatto dopo invece che prima: con il tetto che scende al 45%
--- ogni tre secondi e mezzo la vettura non arriva mai in alto, e passa meta del tempo a frenare.
--- Il tetto medio restava 170, la velocita vera scendeva a 133, e il giro di ricognizione a
--- Melbourne passava da 128 a 144 secondi. Si era chiesto di renderlo piu veloce.
---
--- Quindi l'onda e asimmetrica: sta in alto piu di quanto stia in basso. Il tetto medio sale
--- sopra il nominale, il giro torna intorno ai 100 secondi, e dentro ogni ciclo ci sono una
--- accelerata lunga fino a quasi 260 e una frenata vera. Che e quello che si voleva.
local WARMUP_PERIOD = 11      -- secondi per ciclo: il tempo di tirare una marcia e rilasciare
local WARMUP_LOW = 0.72       -- il tetto nel rilascio, in quota del ritmo nominale
local WARMUP_HIGH = 1.55      -- il tetto nella spinta
local WARMUP_BIAS = 0.6       -- sotto 1 allunga la spinta e accorcia il rilascio

--- L'andatura di una vettura in ricognizione non e un limite fisso.
---
--- Un gruppo che viaggia tutto alla stessa velocita costante non e un giro di ricognizione: e
--- un corteo. In Formula 1 quel giro serve a portare in temperatura gomme, freni e motore, e
--- lo si fa accelerando e rallentando di continuo, non tenendo una velocita.
---
--- Tutte respirano insieme, con una differenza di pochi centesimi di fase.
---
--- Prima l'onda era sfasata di 0.7 radianti per vettura, perche "chi accelera e chi rallenta
--- non sono mai gli stessi" sembrava piu vivo. Era la causa dei contatti in ricognizione, e il
--- conto lo dice: con l'onda sfasata due vetture vicine si trovano ai due estremi dell'ampiezza
--- nello stesso istante, cioe con decine di km/h di differenza a venticinque metri di distanza.
--- Il controllo del distacco non puo rimediare, perche corregge mezzo km/h per ogni metro di
--- scarto: per togliere sessanta km/h di avvicinamento gli servirebbero centoventi metri di
--- errore, che non ha. Quindi tamponava.
---
--- In fase invece la velocita **assoluta** cambia tanto - ed e quella che scalda - mentre quella
--- **relativa** fra una vettura e l'altra resta piccola, e i venticinque metri si mantengono. E
--- anche quello che fa il gruppo vero, dove ognuno segue chi ha davanti: il plotone si allunga e
--- si raccoglie tutto insieme. La stessa scelta e gia stata fatta per la serpentina, e per la
--- stessa ragione.
local function warmupPace(index, pace)
  -- Da seno a quota 0..1, poi l'esponente sposta il tempo passato in alto.
  local t = warmupClock + index * 0.12
  local onda = (1 + math.sin(2 * math.pi * t / WARMUP_PERIOD)) * 0.5
  onda = onda ^ WARMUP_BIAS

  return pace * (WARMUP_LOW + (WARMUP_HIGH - WARMUP_LOW) * onda)
end

local function formationPace(index, pace)
  if state.fPhase ~= 'Rolling' then return pace end

  -- Si lascia la griglia uno alla volta, in ordine di schieramento.
  --
  -- Partendo tutti insieme il gruppo resta incollato per tutto il giro e arriva in griglia in
  -- blocco. In Formula 1 si parte scaglionati proprio per prendere spazio: chi e dietro
  -- aspetta il suo turno e poi recupera.
  local car = ac.getCar(index)
  local grid = car ~= nil and (car.racePosition or 1) or 1
  -- Un km/h, non zero: zero verrebbe letto come "nessun limite", cioe l esatto contrario.
  -- E lo stesso valore con cui si tiene ferma la safety car nella sua piazzola.
  if rollingTimer < (grid - 1) * STAGGER_SECONDS then return 1 end

  -- Il ritmo chiesto dal regolamento, prima che l'onda lo tocchi: i limiti del controllo del
  -- distacco si misurano su questo. Misurarli sul ritmo ondeggiato significava moltiplicare due
  -- scostamenti - l'onda e il recupero - e lasciare passare velocita al doppio del previsto.
  local nominale = pace
  pace = warmupPace(index, pace)

  local gap = gapAhead[index]
  if gap == nil then return pace end   -- chi apre il gruppo tiene il ritmo previsto

  local error_ = gap - FORMATION_GAP
  if math.abs(error_) < FORMATION_DEADBAND then return pace end   -- gia a posto, non toccare

  -- Mezzo km/h per ogni metro di scarto, sopra la velocita di chi precede.
  local target = (speedAhead[index] or nominale) + error_ * 0.5

  -- Chi e rimasto indietro deve poter andare PIU FORTE del ritmo per riprendere.
  --
  -- Qui c era scritto math.min(pace, target), cioe nessuno puo mai superare il ritmo nominale.
  -- Il commento diceva che alzare il tetto non serviva. Era sbagliato, e il conto lo dimostra:
  -- con quel tetto ogni vettura puo solo essere piu lenta di quella davanti, mai piu veloce,
  -- quindi il ritardo si somma scendendo lungo la fila e non si recupera mai. Il giro di
  -- ricognizione a Melbourne ci metteva quattro minuti invece di due e mezzo.
  --
  -- Il tetto resta - non si vuole che partano a razzo - ma sopra il ritmo, non sul ritmo.
  return math.max(nominale * 0.65, math.min(nominale * WARMUP_HIGH, target))
end

-- ---------------------------------------------------------------------------------------------
-- La griglia
--
-- Dove sia la propria piazzola non lo dice nessuna API: lo si impara guardando. Prima del via
-- le vetture sono ferme ognuna nella sua, quindi basta fotografare posizione e direzione in quel
-- momento e si ha la geometria esatta della griglia, pista per pista, senza doverla cercare nei
-- file del circuito.
-- ---------------------------------------------------------------------------------------------

local gridSlots = {}          -- indice vettura -> { pos, look }
local gridCaptured = false
local gridState = { known = false, along = 0, across = 0, distance = 0, inside = false }

--- Fotografa la griglia: una volta sola, e solo mentre sono tutte ferme in piazzola.
---
--- La condizione "sono tutte ferme" non e un di piu, e tutto il punto. Prima qui si guardava
--- sim.isSessionStarted, dando per scontato che diventasse vero al via. Non e cosi: resta
--- falso per tutto il giro di formazione, e la fotografia veniva rifatta a ogni fotogramma
--- mentre le vetture giravano il circuito. L'ultima le coglieva sparse in pista, ognuna
--- rivolta dove capitava, e quella diventava "la griglia": al momento di schierarle venivano
--- messe dove si trovavano un attimo prima, tutte girate dalla parte sbagliata, e da li non
--- ripartivano piu.
---
--- Le vetture ferme tutte insieme capitano in un momento solo di una gara: prima del via,
--- in griglia. Ed e esattamente la fotografia che ci serve.
local function captureGrid()
  if gridCaptured then return end

  local sim = ac.getSim()
  if sim == nil or sim.raceSessionType ~= ac.SessionType.Race then return end

  local total = sim.carsCount or 1
  local ferme = true
  for i = 0, total - 1 do
    local car = ac.getCar(i)
    if car ~= nil and ((car.speedKmh or 0) > 3 or (car.lapCount or 0) > 0) then
      ferme = false
      break
    end
  end

  if not ferme then
    -- Si sono mosse. Se una fotografia buona l'avevamo gia presa la si tiene per sempre;
    -- se non l'abbiamo presa si rinuncia, e le vetture non verranno spostate da nessuno.
    -- Non schierarle e spiacevole, schierarle a casaccio rompe la gara.
    -- Anche senza fotografia si chiude la cattura: una sosta successiva non e una griglia.
    gridCaptured = true
    return
  end

  for i = 0, total - 1 do
    local car = ac.getCar(i)
    if car ~= nil and car.position ~= nil and car.look ~= nil and car.isInPitlane ~= true then
      gridSlots[i] = {
        pos = vec3(car.position.x, car.position.y, car.position.z),
        look = vec3(car.look.x, 0, car.look.z):normalize()
      }
    end
  end
end

--- Quanto manca alla piazzola, scomposto lungo la pista e di lato: due numeri che si capiscono
--- al volo molto meglio di una distanza sola ("sei lungo di due metri" invece di "sei a due").
local function measureGrid()
  gridState.known = false
  local slot = gridSlots[0]
  local car = ac.getCar(0)
  if slot == nil or car == nil then return end

  local d = vec3(car.position.x - slot.pos.x, 0, car.position.z - slot.pos.z)
  local right = vec3(-slot.look.z, 0, slot.look.x)

  gridState.along = d:dot(slot.look)
  gridState.across = d:dot(right)
  gridState.distance = math.sqrt(gridState.along ^ 2 + gridState.across ^ 2)
  gridState.inside = math.abs(gridState.along) <= state.gridAlong
                 and math.abs(gridState.across) <= state.gridAcross
                 and (car.speedKmh or 0) < 3
  gridState.known = true
end

local commands = {}          -- nome pilota -> comando
local indexByName = {}       -- nome pilota -> indice vettura
local compoundCache = {}     -- indice vettura -> { codice -> indice mescola }
local pitRequested = {}      -- indice vettura -> true finche la sosta non e avvenuta
local timeSinceRead = 99
local timeSinceUpdate = 99
local connected = false
local lastSequence = -1
local standings = {}         -- classifica compatta per l'HUD
local weatherPoints = {}
local weatherMinute = 0
local statePath = nil
local physicsOk = nil
local physicsNotice = nil
local safetyCarParked = false
local appliedCount = 0
local unmatched = {}          -- nomi ricevuti che non hanno trovato una vettura in pista
local paceLimit = {}          -- indice vettura -> limite di velocita attuale, portato a rampa
local lastDt = 0.016          -- durata dell'ultimo fotogramma, per le rampe

-- ---------------------------------------------------------------------------------------------
-- Lettura dello stato
-- ---------------------------------------------------------------------------------------------

--- Custom Shaders Patch limita i percorsi leggibili dalle app, e quali siano dipende dalla
--- versione. Il plugin pubblica in piu posti: qui si prova ognuno finche uno risponde.
local attempts = {}

local function candidatePaths()
  local list = {}

  -- Quello che torna da ac.getFolder non e per forza una stringa Lua: puo essere un oggetto
  -- che la rappresenta. Si converte e basta, invece di controllarne il tipo.
  local function add(id, suffix)
    if id == nil then return end
    local ok, base = pcall(ac.getFolder, id)
    if not ok or base == nil then return end
    local text = tostring(base)
    if text ~= '' then list[#list + 1] = text .. suffix end
  end

  add(ac.FolderID.ACDocuments, '\\Pitwall\\race_control.txt')
  add(ac.FolderID.Documents, '\\Pitwall\\live\\race_control.txt')
  add(ac.FolderID.Root, '\\apps\\lua\\pitwall\\live\\race_control.txt')
  return list
end

local function readRaceControl()
  if statePath then return io.load(statePath, nil) end

  attempts = {}
  for _, path in ipairs(candidatePaths()) do
    local raw = io.load(path, nil)
    if raw ~= nil and raw ~= '' then
      statePath = path
      return raw
    end
    attempts[#attempts + 1] = path
  end
  return nil
end

local function split(text, separator)
  local out = {}
  for piece in string.gmatch(text, '([^' .. separator .. ']+)') do out[#out + 1] = piece end
  return out
end

--- Una riga "ai=" e fatta di campi separati da barra verticale, nell'ordine in cui il core li
--- serializza: nome, livello, aggressivita, velocita massima, box, mescola, scostamento,
--- secondi di arresto, safety car, fase, riscaldamento gomme, fase del giro, attesa ai box,
--- permesso DRS, giri di benzina.
local function parseCommand(payload)
  local f = {}
  local i = 1
  for piece in string.gmatch(payload .. '|', '([^|]*)|') do f[i] = piece; i = i + 1 end
  if not f[1] or f[1] == '' then return nil end
  return {
    name = f[1],
    level = tonumber(f[2]) or 95,
    aggression = tonumber(f[3]) or 50,
    topSpeed = tonumber(f[4]) or 0,
    pit = f[5] == '1',
    compound = f[6] or '',
    offset = tonumber(f[7]) or 0,
    stop = tonumber(f[8]) or 0,
    isSafetyCar = f[9] == '1',
    phase = f[10] or '',
    warmup = f[11] == '1',
    lapKind = f[12] or '',
    holdInPit = f[13] == '1',
    drsAllowed = f[14] ~= '0',
    fuelLaps = tonumber(f[15]) or 0
  }
end

local function parse(text)
  local newCommands = {}
  state.yellow = {}
  weatherPoints = {}
  standings = {}

  for line in string.gmatch(text, '([^\n]+)') do
    local key, value = string.match(line, '^([%w_]+)=(.*)$')
    if key == 'ai' then
      local c = parseCommand(value)
      if c then newCommands[c.name] = c end
    elseif key == 'status' then state.status = value
    elseif key == 'text' then state.text = value
    elseif key == 'delta' then state.delta = tonumber(value) or 0
    elseif key == 'targetkmh' then state.targetKmh = tonumber(value) or 0
    elseif key == 'pitclosed' then state.pitClosed = value == '1'
    elseif key == 'message' then state.message = value
    elseif key == 'penalties' then state.penalties = tonumber(value) or 0
    elseif key == 'position' then state.position = tonumber(value) or 0
    elseif key == 'lap' then state.lap = value
    elseif key == 'lingua' then inInglese = value == 'en'
    elseif key == 'scphase' then state.scPhase = value
    elseif key == 'seq' then state.seq = tonumber(value) or 0
    elseif key == 'wxmin' then weatherMinute = tonumber(value) or 0
    elseif key == 'wx' then weatherPoints[#weatherPoints + 1] = value
    elseif key == 'fphase' then
      -- La griglia si azzera sull'evento di AC, non sul ritardo del bridge SimHub.
      state.fPhase = value
    elseif key == 'auto' then state.autopilot = value == '1'
    elseif key == 'lights' then state.lights = tonumber(value) or 0
    elseif key == 'pitstart' then state.pitStart = value == '1'
    elseif key == 'pitstartdelay' then state.pitStartDelay = math.max(0, tonumber(value) or 12)
    elseif key == 'qseg' then state.qSegment = value
    elseif key == 'qleft' then state.qLeft = tonumber(value) or 0
    elseif key == 'qsurv' then state.qSurviving = tonumber(value) or 0
    elseif key == 'drs' then state.drsEnabled = value == '1'
    elseif key == 'drsnotice' then state.drsNotice = value
    elseif key == 'drsgap' then state.drsGap = tonumber(value) or 0
    elseif key == 'drsplayer' then state.drsPlayer = value == '1'
    elseif key == 'fuelrun' then state.fuelRunMargin = tonumber(value) or 1.15
    elseif key == 'fuelrace' then state.fuelRaceMargin = tonumber(value) or 1.07
    elseif key == 'fuelres' then state.fuelReserve = tonumber(value) or 2
    elseif key == 'pos' then
      -- La barra finale aggiunta apposta: senza, un campo vuoto in coda sparirebbe e i campi
      -- dopo di lui scalerebbero di posto.
      local f = {}
      for piece in string.gmatch(value .. '|', '([^|]*)|') do f[#f + 1] = piece end
      standings[#standings + 1] = {
        position = tonumber(f[1]) or 0, name = f[2] or '', gap = f[3] or '',
        compound = f[4] or '', age = tonumber(f[5]) or 0,
        player = f[6] == '1', pit = f[7] == '1',
        eliminated = f[8] == '1', dropZone = f[8] == '2'
      }
    elseif key == 'gridslot' then state.gridSlot = tonumber(value) or 0
    elseif key == 'gridnotice' then state.gridNotice = value
    elseif key == 'giveback' then state.giveBack = tonumber(value) or 0
    elseif key == 'gbleft' then state.giveBackLeft = tonumber(value) or 0
    elseif key == 'gtalong' then state.gridAlong = tonumber(value) or 2.5
    elseif key == 'gtacross' then state.gridAcross = tonumber(value) or 1.4
    elseif key == 'yellow' and value ~= '' then
      for _, s in ipairs(split(value, ',')) do state.yellow[#state.yellow + 1] = tonumber(s) end
    end
  end

  commands = newCommands

  -- Dopo il cambio sessione il file puo ancora descrivere il vecchio semaforo per qualche
  -- fotogramma. Si aspetta Idle/Rolling prima di accettare un altro schieramento o pit start.
  if formationAwaitingStart then
    if state.fPhase == 'Idle' or state.fPhase == 'Rolling' then
      formationAwaitingStart = false
    else
      state.fPhase = 'Idle'
      state.pitStart = false
      state.lights = 0
    end
  end

  -- Il piano meteo va passato al controller di Custom Shaders Patch. Non glielo si puo lasciare
  -- leggere da solo: gira in un contesto piu chiuso e il disco potrebbe non essergli concesso.
  -- Questo canale fra script invece e fatto apposta per scambiarsi dati.
  ac.store('.pitwall.wx', table.concat(weatherPoints, ';'))
  ac.store('.pitwall.wx.minute', weatherMinute)
  ac.store('.pitwall.wx.seq', state.seq)
end

local function refresh(dt)
  timeSinceRead = timeSinceRead + dt
  timeSinceUpdate = timeSinceUpdate + dt
  if timeSinceRead < REFRESH_INTERVAL then return end
  timeSinceRead = 0

  local content = readRaceControl()
  if content == nil or content == '' then
    statePath = nil
    connected = timeSinceUpdate < STALE_AFTER
    return
  end

  parse(content)

  -- Leggere il file non basta: se il plugin si ferma il file resta dov'e, con dentro l'ultima
  -- fotografia. Il contatore che sale e l'unica prova che dall'altra parte c'e ancora qualcuno.
  if state.seq ~= lastSequence then
    lastSequence = state.seq
    timeSinceUpdate = 0
  end
  connected = timeSinceUpdate < STALE_AFTER
end

-- ---------------------------------------------------------------------------------------------
-- Corrispondenza fra nomi e vetture
-- ---------------------------------------------------------------------------------------------

--- Chiave con cui si confrontano i nomi dei piloti.
---
--- Normalizzata, perche le due estremita avevano regole diverse: il plugin confronta ignorando
--- maiuscole e minuscole, questo script le confrontava esatte. Uno spazio in coda o una lettera
--- maiuscola diversa faceva sparire una vettura dai comandi, senza nessun avviso.
local function nameKey(name)
  return (tostring(name or ''):gsub('^%s+', ''):gsub('%s+$', '')):lower()
end

local function rebuildIndices()
  indexByName = {}
  safetyCarIndex = nil
  local total = ac.getSim().carsCount
  for i = 0, total - 1 do
    local name = ac.getDriverName(i)
    if name and name ~= '' then
      indexByName[nameKey(name)] = i
      if nameKey(name):find('safety car', 1, true) then safetyCarIndex = i end
    end
  end
end

--- Ogni mod nomina le mescole a modo suo. Qui si leggono i nomi brevi che il gioco espone per
--- ogni set e si cerca quello che inizia con la lettera chiesta dalla strategia.
local function compoundIndexFor(carIndex, code)
  if not code or code == '' then return nil end
  local cache = compoundCache[carIndex]
  if not cache then
    cache = {}
    for idx = 0, 9 do
      local name = ac.getTyresName(carIndex, idx)
      if not name or name == '' then break end
      local letter = name:sub(1, 1):upper()
      if cache[letter] == nil then cache[letter] = idx end
      -- Anche i nomi estesi: "Soft", "Medium", "Wet" ...
      local lower = name:lower()
      if lower:find('inter', 1, true) and cache['I'] == nil then cache['I'] = idx end
      if lower:find('wet', 1, true) and cache['W'] == nil then cache['W'] = idx end
    end
    compoundCache[carIndex] = cache
  end
  return cache[code:sub(1, 1):upper()]
end

--- Che sessione sta girando davvero, secondo Assetto Corsa.
---
--- Non secondo Pitwall: secondo il gioco. Serve come controprova, perche il core puo
--- sbagliarsi sul tipo di sessione - gli arriva da SimHub, e nei primi fotogrammi di una
--- sessione nuova puo ancora riportare quella precedente - mentre il gioco no.
local acIsRace = false

--- Vero solo quando il gioco dichiara prove libere o qualifica.
---
--- Due bandierine invece di una, e non e ridondanza: "non e una gara" e "e una sessione di
--- prove" sono cose diverse, perche c'e un terzo caso - non lo sappiamo ancora - che capita
--- nei primi fotogrammi, mentre la sessione si carica. Le azioni che fanno danno se sbagliate
--- - tenere ferma una vettura, riscriverle la benzina - si fanno solo quando siamo sicuri in
--- positivo. Nel dubbio non si tocca niente.
local acIsPractice = false

-- ---------------------------------------------------------------------------------------------
-- DRS
-- ---------------------------------------------------------------------------------------------

--- Le zone DRS del tracciato.
---
--- Assetto Corsa le tiene nei dati della pista, in drs_zones.ini: per ogni zona un punto di
--- rilevamento, uno di apertura e uno di chiusura, espressi come frazione del giro. Il
--- rilevamento viene prima dell'apertura, ed e li che in Formula 1 si misura il distacco.
---
--- Si leggono una volta e si tengono: sono dati del tracciato, non cambiano durante la gara.
local drsZones = nil

local function loadDrsZones()
  if drsZones ~= nil then return drsZones end
  drsZones = {}

  local ok = pcall(function()
    local cfg = ac.INIConfig.trackData('drs_zones.ini')
    if cfg == nil then return end

    for i = 0, 15 do
      local section = 'ZONE_' .. i
      local detection = cfg:get(section, 'DETECTION', -1)
      if type(detection) ~= 'number' or detection < 0 then break end
      drsZones[#drsZones + 1] = {
        detection = detection,
        from = cfg:get(section, 'START', -1),
        to = cfg:get(section, 'END', -1)
      }
    end
  end)

  if not ok then drsZones = {} end
  return drsZones
end

--- Vero se fra i due fotogrammi la vettura ha superato il punto p del giro.
---
--- Il caso difficile e il traguardo: se fra un fotogramma e l'altro il giro si e chiuso, la
--- posizione e tornata indietro da 0.99 a 0.01, e un confronto ingenuo direbbe che non e stato
--- superato niente, mentre invece e stato superato tutto il tratto finale e quello iniziale.
local function crossedPoint(prev, now, p)
  if now >= prev then return prev < p and p <= now end
  return p > prev or p <= now
end

--- Il distacco da chi precede, in secondi.
---
--- I metri lungo la pista li misura gia measureGaps; qui si dividono per la velocita. Chi non
--- ha nessuno davanti non riceve niente, ed e giusto: il leader il DRS non lo ha mai.
local function gapSecondsAhead(index, car)
  local metres = gapAhead[index]
  if metres == nil then return nil end

  local speed = car.speedKmh or 0
  if speed < 60 then return nil end
  return metres / (speed / 3.6)
end

local drsLastSpline = {}   -- indice vettura -> posizione sul giro al fotogramma precedente
local drsGrant = {}        -- indice vettura -> DRS concesso per la zona che sta affrontando
local drsWritten = {}      -- ultimo permesso scritto, per non riscriverlo a ogni fotogramma
local drsReassert = 0      -- da quanto non riscriviamo i permessi comunque, in secondi
local drsDenied = false    -- vero se a qualcuno abbiamo negato il DRS e va restituito

--- Aggiorna i permessi al passaggio dai punti di rilevamento.
---
--- Una volta per fotogramma, per tutte le vetture. Il permesso vale per la zona che segue ed e
--- riscritto al rilevamento successivo: un solo permesso per vettura sembra approssimato, ma
--- rilevamenti e zone si alternano sempre - rilevamento, zona, rilevamento, zona - quindi
--- tenerne uno solo e esattamente quello che serve.
local function updateDrsGrants()
  if state.drsGap <= 0 then return end

  local zones = loadDrsZones()
  if #zones == 0 then return end

  local sim = ac.getSim()
  if sim == nil then return end

  for i = 0, (sim.carsCount or 1) - 1 do
    local car = ac.getCar(i)
    if car ~= nil then
      local now = car.splinePosition or 0
      local prev = drsLastSpline[i]
      drsLastSpline[i] = now

      if prev ~= nil then
        for _, zone in ipairs(zones) do
          if crossedPoint(prev, now, zone.detection) then
            local seconds = gapSecondsAhead(i, car)
            drsGrant[i] = seconds ~= nil and seconds <= state.drsGap
          end
        end
      end
    end
  end
end

--- Vero se la regola del secondo si puo applicare adesso.
---
--- Serve il permesso del core - che la chiede solo in gara - e i punti di rilevamento del
--- tracciato. Se quelli non si sono letti, la regola si spegne invece di valere: applicarla
--- senza sapere dove sono i rilevamenti vorrebbe dire negare il DRS a tutto il gruppo per
--- tutta la gara, e non vedere mai un'ala aperta senza capire perche.
local function gapRuleActive()
  return state.drsGap > 0 and #loadDrsZones() > 0
end

--- Concede o nega il DRS a una vettura.
---
--- allowCarDRS puo solo togliere il permesso: se in quel punto della pista il DRS non e
--- previsto, questa chiamata non lo apre comunque. Ed e esattamente quello che serve, perche
--- l'ala non si deve mai aprire fuori dalle zone. Aprirla si potrebbe, con setCarDRS, e sarebbe
--- sbagliato: per questo quella funzione qui non si usa.
local function applyDrs(index, c)
  local allowed = c.drsAllowed ~= false

  -- La regola del secondo al rilevamento. Il core la chiede solo in gara, perche in prove e
  -- qualifica il DRS in zona e libero per regolamento.
  if allowed and gapRuleActive() then allowed = drsGrant[index] == true end

  if drsWritten[index] ~= allowed then
    drsWritten[index] = allowed
    physics.allowCarDRS(index, allowed)
    if not allowed then drsDenied = true end
  end
end

--- Una volta al secondo i permessi si riscrivono comunque, anche se non sono cambiati.
---
--- Scriverli solo quando cambiano basterebbe se il permesso restasse dove lo si mette, e
--- cosi dovrebbe essere. Ma lo stesso ragionamento l'avevo fatto per lo scostamento laterale
--- e per il divieto di ritirarsi, e in entrambi i casi mi sbagliavo. Una riscrittura al
--- secondo non costa niente e toglie l'unico modo in cui questa cosa potrebbe rompersi in
--- silenzio: ali aperte quando non si deve, e nessuna traccia del perche.
local function refreshDrsWrites(dt)
  drsReassert = drsReassert + (dt or 0)
  if drsReassert < 1 then return end
  drsReassert = 0
  drsWritten = {}
end

--- Restituisce il DRS a tutti. Da chiamare quando smettiamo di comandare.
---
--- Serve perche il permesso resta appiccicato alla vettura: se ci si ferma a meta gara con
--- qualcuno a cui l'avevamo negato, quello resterebbe senza ala per il resto della sessione.
local function releaseAllDrs()
  -- Si guarda tutto il gruppo e non solo chi ricordiamo di aver bloccato, perche i ricordi
  -- li buttiamo via a ogni riscrittura periodica: fidarsi di quelli vorrebbe dire lasciare
  -- una vettura senza ala per il resto della sessione, senza piu nessuno che possa ridargliela.
  if not drsDenied then return end
  drsDenied = false

  local sim = ac.getSim()
  local total = sim ~= nil and (sim.carsCount or 0) or 0
  for i = 0, total - 1 do
    pcall(function() physics.allowCarDRS(i, true) end)
  end

  drsWritten = {}
  drsGrant = {}
end

--- Il DRS della vettura del giocatore.
---
--- Serve una funzione a parte perche al giocatore non arriva nessun comando: la sua vettura
--- non e guidata dall'IA, quindi il giro che distribuisce i comandi la salta. Ma le regole
--- valgono per tutti - un campionato in cui l'avversario ha il DRS bloccato fino al terzo giro
--- e tu no non e un campionato - e allora il permesso glielo si scrive qui, con la stessa
--- regola che vale per gli altri.
local function applyPlayerDrs()
  if state.drsPlayer ~= true then return end

  local allowed = state.drsEnabled
  if allowed and gapRuleActive() then allowed = drsGrant[0] == true end

  if drsWritten[0] ~= allowed then
    drsWritten[0] = allowed
    pcall(function() physics.allowCarDRS(0, allowed) end)
    if not allowed then drsDenied = true end
  end
end

-- ---------------------------------------------------------------------------------------------
-- Benzina
-- ---------------------------------------------------------------------------------------------

--- Il consumo non si indovina, si misura.
---
--- A ogni giro completato si guarda quanta benzina e sparita e si fa una media mobile. Quello
--- che si impara resta da una sessione all'altra: le prove del venerdi sono esattamente il
--- momento in cui una squadra scopre quanto consuma la macchina su quel circuito, e in gara
--- quel numero serve per decidere quanto caricare.
local fuelStore = ac.storage({ learned = '' })

--- Quanti giri di misura servono per fidarsi del consumo in gara.
---
--- Uno non basta. Un giro dietro la safety car, o un giro di rientro, consumano molto meno
--- del vero: calcolare la benzina di gara su quello vuol dire una vettura ferma a secco
--- all'ultimo giro. Tre giri sono abbastanza per avere una media che regge.
local FUEL_SAMPLES_FOR_RACE = 3

local fuelPerLap = {}        -- indice vettura -> litri al giro misurati
local fuelSamples = {}       -- indice vettura -> su quanti giri e stata fatta la misura
local fuelLapDirty = {}      -- indice vettura -> il giro in corso non vale come misura
local fuelAtLapStart = {}    -- indice vettura -> benzina all'inizio del giro in corso
local fuelLapSeen = {}       -- indice vettura -> ultimo giro contato
local fuelApplied = {}       -- indice vettura -> vero finche non riparte dal box
local fuelSet = {}           -- indice vettura -> litri che le abbiamo messo, per la diagnostica

--- La chiave con cui si ricorda un consumo: modello di vettura e pista.
local function fuelKey(index)
  local id = nil
  pcall(function() id = ac.getCarID(index) end)
  local track = ''
  pcall(function() track = ac.getTrackID() or '' end)
  return (id or ('vettura' .. index)) .. '@' .. track
end

local learnedFuel = nil

--- Legge i consumi imparati, tenuti come "chiave=litri;chiave=litri".
local function loadLearnedFuel()
  if learnedFuel ~= nil then return learnedFuel end
  learnedFuel = {}

  for piece in string.gmatch(fuelStore.learned or '', '([^;]+)') do
    local key, value = string.match(piece, '^(.+)=([%d%.]+)$')
    if key and value then learnedFuel[key] = tonumber(value) end
  end

  return learnedFuel
end

local function saveLearnedFuel()
  local parts = {}
  for key, value in pairs(learnedFuel or {}) do
    parts[#parts + 1] = key .. '=' .. string.format('%.3f', value)
  end
  fuelStore.learned = table.concat(parts, ';')
end

--- Misura il consumo di una vettura, giro per giro.
---
--- Si scartano i valori impossibili. Una sosta rifornisce e darebbe consumo negativo, e la
--- nostra stessa chiamata a setCarFuel darebbe un salto enorme: senza questo filtro il numero
--- imparato sarebbe spazzatura proprio per colpa nostra.
local function learnFuel(index)
  local car = ac.getCar(index)
  if car == nil then return end

  local lap = car.lapCount or 0
  local fuel = car.fuel or 0

  if fuelLapSeen[index] == nil then
    fuelLapSeen[index] = lap
    fuelAtLapStart[index] = fuel
    return
  end

  if lap == fuelLapSeen[index] then return end

  local used = (fuelAtLapStart[index] or fuel) - fuel
  fuelLapSeen[index] = lap
  fuelAtLapStart[index] = fuel

  if used < 0.2 or used > 8 then return end
  if car.isInPitlane == true then return end

  -- Un giro in cui siamo stati noi a limitare la velocita non dice niente sul consumo.
  --
  -- E il difetto che ha prodotto 0.85 litri al giro su una vettura di Formula 1, che ne
  -- consuma due e mezzo: la misura veniva da giri di ricognizione a centodieci all'ora e da
  -- giri di uscita dai box, dove il motore lavora a un terzo. Portata in gara, quel numero
  -- avrebbe mandato tutti a secco a meta corsa.
  if fuelLapDirty[index] then
    fuelLapDirty[index] = nil
    return
  end

  local previous = fuelPerLap[index]
  fuelPerLap[index] = previous == nil and used or (previous * 0.7 + used * 0.3)
  fuelSamples[index] = (fuelSamples[index] or 0) + 1

  local learned = loadLearnedFuel()
  learned[fuelKey(index)] = fuelPerLap[index]
  saveLearnedFuel()
end

--- Il consumo da usare: quello misurato adesso, o quello imparato nelle sessioni precedenti.
local function fuelRateFor(index)
  local now = fuelPerLap[index]
  if now ~= nil and now > 0 then return now end

  local learned = loadLearnedFuel()[fuelKey(index)]
  if learned ~= nil and learned > 0 then
    fuelPerLap[index] = learned
    -- Arriva dalle sessioni precedenti: i giri per misurarlo li avevamo gia fatti, quindi
    -- vale come misura solida anche se in questa sessione non abbiamo ancora girato.
    fuelSamples[index] = math.max(fuelSamples[index] or 0, FUEL_SAMPLES_FOR_RACE)
    return learned
  end

  return nil
end

--- Imbarca la benzina che serve, a vettura ferma.
---
--- setCarFuel annulla il giro in corso, quindi si chiama solo quando non c'e un giro da
--- rovinare: ferma nel box fra due uscite, o in griglia prima del via. Ed e anche l'unico
--- momento in cui avrebbe senso, perche e li che una squadra decide quanto caricare.
local function applyFuel(index, c, racing)
  if c.fuelLaps == nil or c.fuelLaps <= 0 then return end
  if fuelApplied[index] then return end

  -- La benzina del giocatore non si tocca: l'ha scelta lui nell'assetto, e riscriverla gli
  -- annullerebbe anche il giro. Col pilota automatico invece se ne occupa Pitwall, perche al
  -- volante non c'e nessuno a decidere.
  if index == 0 and state.autopilot ~= true then return end

  local car = ac.getCar(index)
  if car == nil then return end
  if (car.speedKmh or 0) > 3 then return end

  -- Ferma non basta. In gara una vettura girata al primo giro e immobile anche lei, e
  -- rifornirla in mezzo alla pista non si fa: si pretende la piazzola prima del via, cioe
  -- nessun giro all'attivo, oppure la corsia box.
  if racing and car.isInPitlane ~= true and (car.lapCount or 0) > 0 then return end

  local maxFuel = car.maxFuel or 0
  if maxFuel <= 0 then return end

  local perLap = fuelRateFor(index)

  -- In gara serve una misura su piu giri. Senza, si lascia la benzina come l'ha messa
  -- Assetto Corsa: portare qualche chilo di troppo e un difetto, fermarsi a secco no.
  if racing and (fuelSamples[index] or 0) < FUEL_SAMPLES_FOR_RACE then perLap = nil end

  -- Fuori da prove e qualifica dichiarate non si stima niente: senza un consumo misurato e
  -- senza sapere che sessione sia, riscrivere il serbatoio e un tiro a indovinare su una
  -- cosa che non si puo correggere dopo.
  if perLap == nil and not acIsPractice then return end

  local litres

  if perLap ~= nil then
    local margin = racing and state.fuelRaceMargin or state.fuelRunMargin
    litres = c.fuelLaps * perLap * margin + state.fuelReserve
  else
    -- In prove e qualifica si puo stimare: un'uscita e corta, e il pieno e comunque sbagliato.
    -- Un terzo di serbatoio copre qualunque programma, e da questa uscita si impara il consumo.
    litres = maxFuel * 0.35
  end

  -- In gara si puo alleggerire, ma entro un limite.
  --
  -- Assetto Corsa la benzina per la distanza la mette gia, abbondando. Toglierne un po' rende
  -- le vetture piu veloci nei primi giri ed e giusto; toglierne troppo, per una misura di
  -- consumo sbagliata, vuol dire vetture ferme a secco e una gara rovinata che non si puo
  -- piu aggiustare. Un quinto in meno e il massimo che ci si prende.
  if racing then
    local aBordo = car.fuel or 0
    if aBordo > 0 then litres = math.max(litres, aBordo * 0.80) end
  end

  litres = math.max(state.fuelReserve, math.min(maxFuel, litres))

  local ok = pcall(function() physics.setCarFuel(index, litres) end)
  if ok then
    fuelApplied[index] = true
    fuelSet[index] = litres
  end
end

--- Monta la mescola chiesta, a vettura ferma.
---
--- Le gomme si cambiano dove si cambiano davvero: fermi. Montarle in movimento non si fa, e
--- la funzione che lo permetterebbe per la vettura del giocatore la documentazione di Custom
--- Shaders Patch la segna come ultima spiaggia, con scritto che la fisica puo esplodere.
---
--- Per questo al giocatore si tocca la vettura solo quando al volante c'e una IA. Quando
--- guida lui, la gomma giusta gliela dice l'ingegnere via radio e poi decide lui: e anche
--- cosi che funziona in Formula 1.
local tyresFitted = {}   -- indice vettura -> mescola montata da noi per questa sosta

local function applyTyres(index, c)
  if c.compound == nil or c.compound == '' then return end
  if tyresFitted[index] == c.compound then return end

  local car = ac.getCar(index)
  if car == nil then return end
  if (car.speedKmh or 0) > 3 then return end
  if car.isAIControlled ~= true then return end

  local mescola = compoundIndexFor(index, c.compound)
  if mescola == nil then return end

  if pcall(function() physics.setAITyres(index, mescola) end) then
    tyresFitted[index] = c.compound
  end
end

--- Si riapre la possibilita di rifornire quando la vettura e tornata in pista.
local function resetFuelHold(index, car)
  if car ~= nil and car.isInPitlane ~= true and (car.speedKmh or 0) > 30 then
    fuelApplied[index] = nil
    tyresFitted[index] = nil
  end
end

-- ---------------------------------------------------------------------------------------------
-- Applicazione dei comandi
-- ---------------------------------------------------------------------------------------------

--- Un "no" appena caricata la sessione non e detto che sia definitivo: Custom Shaders Patch
--- puo non aver ancora finito di mettersi in moto. Il permesso si tiene da conto solo quando
--- arriva, e il rifiuto si ricontrolla per mezzo minuto prima di arrendersi - altrimenti un
--- singolo frame sfortunato spegne safety car e giro di formazione per tutta la gara.
local physicsRetry = 30

local function physicsAllowed()
  if physicsOk == true then return true end

  if physicsRetry > 0 then
    local ok, allowed = pcall(function() return physics.allowed() end)
    if ok and allowed == true then
      physicsOk = true
      physicsRetry = 0
      physicsNotice = nil
      return true
    end
    physicsNotice = 'Script di physics non ancora disponibili, riprovo...'
    return false
  end

  if physicsOk == nil then
    physicsOk = false
    physicsNotice = 'IA non pilotabile: questa pista non consente gli script di physics'
  end
  return false
end

--- Scrivere nei comandi di una vettura vuol dire scriverli TUTTI.
---
--- Questo e il difetto che ha tenuto ferme le vetture al semaforo verde per giorni, ed e
--- stato il sintomo a smascherarlo: le IA alzavano i giri del motore senza muoversi. Un
--- freno non fa quel rumore. Una frizione staccata si.
---
--- ac.overrideCarControls restituisce la struttura completa dei comandi, e noi ne scrivevamo
--- uno solo - il freno - lasciando gli altri al valore con cui nasce la struttura. Ma la
--- documentazione dice che per la frizione il valore finale e il MINIMO fra l'originale e il
--- nostro, e che zero vuol dire frizione staccata: lasciarla a zero significa togliere la
--- trasmissione alla vettura per sempre. Lo sterzo ha la stessa trappola al contrario: va
--- messo a math.huge per dire "lascia stare quello vero", altrimenti si forza dritto.
---
--- Da qui in avanti i comandi si scrivono solo con queste due funzioni.
local function controlsNeutral(index)
  local c = ac.overrideCarControls(index)
  if c == nil then return nil end

  c.gas = 0              -- il massimo fra questo e l'originale: non toglie niente
  c.brake = 0
  c.handbrake = 0
  c.clutch = 1           -- il minimo fra questo e l'originale: frizione attaccata
  c.steer = math.huge    -- lascia lo sterzo a chi guida
  return c
end

--- Inchioda una vettura: freno a fondo e freno a mano, tutto il resto neutro.
local function controlsHold(index)
  local c = controlsNeutral(index)
  if c == nil then return nil end

  c.brake = 1
  c.handbrake = 1
  return c
end

--- Vettura ferma col motore su di giri, pronta a partire.
---
--- E quello che si sente quando si accendono le luci rosse: venti motori che salgono insieme
--- e restano li, e la vettura non si muove di un centimetro. Si ottiene staccando la frizione
--- - cosi i giri salgono senza arrivare alle ruote - e tenendo il freno.
---
--- La frizione staccata e esattamente la condizione che per giorni ha tenuto le vetture
--- inchiodate al verde, quando ci finivano per sbaglio. Qui ci si va apposta e per pochi
--- secondi, e si esce di li con controlsNeutral, che la riattacca.
local function controlsLaunch(index, rev)
  local c = controlsHold(index)
  if c == nil then return nil end

  c.gas = math.max(0, math.min(1, rev or 0))
  c.clutch = 0
  return c
end

--- Vetture tenute ferme nel loro box perche il turno non e il loro.
local pitHeld = {}

--- Tiene una vettura ferma nel suo box.
---
--- Serve perche in una sessione vera non girano tutti per un'ora di fila: si esce a turno, e
--- fra un turno e l'altro si sta dentro. Assetto Corsa non lo sa fare, le sue IA escono
--- subito e non rientrano piu.
---
--- Due attenzioni. Non si teletrasporta niente, perche la vettura e gia dove deve stare. E non
--- si inchioda una vettura che sta ancora percorrendo la corsia: li bloccherebbe tutti gli
--- altri. Si ferma solo quella ferma o quasi, che e appunto chi aspetta il suo turno.
local function holdInPitBox(index)
  local car = ac.getCar(index)
  if car == nil then return false end
  if (car.speedKmh or 0) > 20 and car.isInPit ~= true then return false end

  pcall(function()
    -- Niente setAINoInput, per la stessa ragione per cui non c'e nella tenuta in griglia:
    -- mette la vettura in "parked state" e da li non si esce passandogli false. Una vettura
    -- trattenuta ai box in qualifica restava morta per tutto il resto della sessione di
    -- gioco - gara compresa - e al via non partiva. L'avevo corretto in griglia e lasciato
    -- qui: lo stesso difetto in due posti, corretto in uno solo.

    -- Senza questa riga una IA ferma viene considerata in panne e si ritira: dopo qualche
    -- minuto di attesa ai box il gruppo comincerebbe a sparire.
    physics.preventAIFromRetiring(index)

    physics.setAITopSpeed(index, 1)
    physics.setAIThrottleLimit(index, 0)

    -- "Fai frenare l'IA per un tempo": si rinnova a ogni fotogramma e si annulla con zero.
    physics.setAIStopCounter(index, 0.5)

    -- Il freno e l'unica leva che ferma davvero: fra il valore dell'IA e il nostro vince il
    -- massimo, mentre il limite di velocita agisce solo sul gas.
    controlsHold(index)
  end)

  pitHeld[index] = true
  return true
end

--- Rimette in moto una vettura che stavamo tenendo nel box.
local function releasePitHold(index)
  if not pitHeld[index] then return end
  pitHeld[index] = nil
  pcall(function()
    -- Una vettura tenuta ferma viene messa a dormire dal motore fisico e da li non riparte,
    -- qualunque comando le si dia.
    physics.awakeCar(index)
    physics.setAINoInput(index, false, false)
    physics.setAIStopCounter(index, 0)
    physics.setAITopSpeed(index, 1e9)
    physics.setAIThrottleLimit(index, 1)
    controlsNeutral(index)
  end)
end

--- Impone a una vettura il ritmo che le tocca, in km/h. Zero vuol dire nessun limite.
---
--- Il limite si abbassa a rampa, non a gradino. Non e un freno: agisce sul gas, quindi una
--- vettura a 250 km/h a cui si dice "80" rilascia e basta, e continua per centinaia di metri.
--- Partendo dalla velocita attuale e scendendo di venti km/h al secondo la decelerazione la fa
--- davvero, e nel frattempo si aiuta col limitatore di gas.
local function applyPace(index, topSpeed, scaldando)
  if topSpeed and topSpeed > 0 then
    local wanted = formationPace(index, topSpeed)

    -- Fuori dalla ricognizione il ritmo lo ondeggia questa riga.
    --
    -- Un giro di uscita a velocita costante non scalda niente: la gomma si scalda col lavoro -
    -- accelerare, frenare, caricare - e a ritmo fisso di lavoro non ce n'e. Chi esce dai box
    -- in Formula 1 non tiene una velocita, accelera e rallenta di continuo apposta.
    if scaldando and state.fPhase ~= 'Rolling' then wanted = warmupPace(index, wanted) end
    local car = ac.getCar(index)
    local speed = car and car.speedKmh or wanted
    local current = paceLimit[index] or speed
    local step = 20 * lastDt

    if current > wanted then current = math.max(wanted, current - step)
    else current = math.min(wanted, current + step) end
    paceLimit[index] = current

    physics.setAITopSpeed(index, current)
    -- Finche c'e da smaltire velocita si chiude anche il gas: e la leva che usano le app di
    -- virtual safety car per rallentare davvero invece di sperare nel rilascio.
    physics.setAIThrottleLimit(index, speed > current + 15 and 0.25 or 1)

    -- E si prende nota: su questo giro il consumo non si misura.
    fuelLapDirty[index] = true
  else
    paceLimit[index] = nil
    physics.setAITopSpeed(index, 1e9)
    physics.setAIThrottleLimit(index, 1)
  end
end

--- Vero se in questo fotogramma la vettura del giocatore ha ricevuto un comando dal core.
local playerCommanded = false

--- Il ritmo della vettura del giocatore quando a guidarla e l'IA di Assetto Corsa.
---
--- Il core non le manda nessun comando: da per scontato che al volante ci sia una persona, e
--- il pilota automatico di Pitwall e spento. Ma Assetto Corsa ha la sua opzione per farsi
--- sostituire al volante, e allora quella vettura partecipa alla procedura di partenza come
--- tutte le altre: viene messa in piazzola e tenuta ferma.
---
--- Al rilascio pero si ritrovava senza nessun limite di velocita, mentre il resto del gruppo
--- era a centodieci all'ora di ricognizione. Partiva a tutta in mezzo alle altre, e sembrava
--- che ci andasse addosso apposta. Qui le si da lo stesso ritmo degli altri.
local function paceCarUnderGameAi(index)
  local car = ac.getCar(index)
  if car == nil or car.isAIControlled ~= true then
    paceLimit[index] = nil
    return
  end

  applyPace(index, state.targetKmh)
end

local function applyRacer(index, c)
  applyDrs(index, c)

  -- Si tiene fermo qualcuno ai box solo in prove o qualifica dichiarate dal gioco.
  --
  -- Il blocco serve dove si esce a turno. In gara non ha nessun senso, e il danno che fa e
  -- totale: a Melbourne il core ha creduto per un po' di essere ancora in qualifica, ha
  -- chiesto di tenere tutti dentro, e la gara e partita con le vetture ferme in garage. Un
  -- difetto di riconoscimento della sessione e diventato una gara morta.
  --
  -- La condizione e in positivo e non "se non e una gara": nei primi fotogrammi il tipo di
  -- sessione non e ancora noto, e in quel momento "non e una gara" sarebbe vero e porterebbe
  -- a inchiodare tutti in griglia.
  if c.holdInPit and not acIsPractice then
    releasePitHold(index)
    c.holdInPit = false
  end

  if c.holdInPit then
    -- La gomma del programma si monta adesso, mentre e ferma: e il momento in cui una squadra
    -- vera la cambia, non mentre la vettura e in pista.
    if pitHeld[index] == nil then
      local compound = compoundIndexFor(index, c.compound)
      if compound then physics.setAITyres(index, compound) end
    end
    -- Se e ancora in movimento nella corsia si lascia finire: la si fermera al fotogramma
    -- in cui e arrivata.
    if holdInPitBox(index) then return end
  else
    releasePitHold(index)
  end

  -- Il livello IA di CSP va da 0 a 2, dove 1 e il 100% del launcher.
  physics.setAILevel(index, math.max(0.5, math.min(1.2, c.level / 100)))
  -- L'aggressivita va da 0 a 1, e il 100% del launcher vale 0.95.
  physics.setAIAggression(index, math.max(0, math.min(0.95, c.aggression / 100 * 0.95)))

  applyPace(index, c.topSpeed, c.warmup)

  -- La cautela non si tocca piu. Avevo alzato questo valore a 6 credendo regolasse la distanza
  -- da chi sta davanti, ma agisce sulla prudenza generale dell'IA - margini di frenata,
  -- velocita in curva - e sei volte il normale faceva strisciare tutto il gruppo. Le distanze
  -- si ottengono con l'andatura, non con la paura.

  if c.stop and c.stop > 0 then
    physics.setAIStopCounter(index, c.stop)
  end

  local offset = c.offset or 0

  -- In ricognizione le vetture zigzagano per scaldare le gomme. La prima versione le faceva
  -- tamponare per tre motivi: l'ampiezza era fuori scala, ognuna aveva la sua fase e quindi si
  -- muovevano una contro l'altra, e all'IA era stato detto di ignorare le altre vetture.
  --
  -- Qui invece la fase e quasi la stessa per tutte: si spostano insieme, come un plotone, e la
  -- distanza laterale fra una e l'altra resta quella di prima. La piccola differenza serve solo
  -- a non farle sembrare un corpo di ballo, e vale pochi centimetri di movimento relativo.
  -- La serpentina serve in due momenti: nella ricognizione prima della partenza, e nel giro di
  -- uscita dai box in prove e qualifica. Il primo lo decide la fase della gara, il secondo lo
  -- dice il comando della singola vettura, perche ognuna esce per conto suo.
  local warming = weaveRamp > 0.01 and 1 or 0
  if c.warmup then warming = 1 end

  if warming > 0 then
    local car = ac.getCar(index)
    local speed = car ~= nil and (car.speedKmh or 0) or 0
    if car ~= nil and speed > 30 and car.isInPitlane ~= true then
      -- Niente serpentine in curva. Il freno era la velocita di imbardata misurata, ma
      -- zigzagare *e* imbardare: appena la vettura cominciava a muoversi si spegneva da sola.
      -- Adesso la curva si deduce dalla pista, non dal comportamento della vettura: lo sterzo
      -- dice dove sta andando il tracciato, e la serpentina non lo falsa.
      local steer = math.abs(car.steer or 0) / math.max(1, car.steerLock or 400)
      local turning = math.min(1, steer * 4)
      local jitter = ((index % 5) - 2) * 0.05
      local strength = c.warmup and 1 or weaveRamp

      -- Quanta serpentina concede questa velocita.
      local fast = 1
      if speed >= WEAVE_NONE_KMH then fast = 0
      elseif speed > WEAVE_FULL_KMH then
        fast = 1 - (speed - WEAVE_FULL_KMH) / (WEAVE_NONE_KMH - WEAVE_FULL_KMH)
      end

      offset = offset + math.sin(weavePhase + jitter) * WEAVE_AMPLITUDE * strength * (1 - turning) * fast
    end
  end

  -- Lo scostamento va riscritto sempre, anche quando torna a zero: scriverlo solo quando serve
  -- lasciava appiccicato alle vetture l'ultimo valore, che se le portavano dietro nella gara.
  -- L'ultimo argomento resta falso: l'IA continua a guardarsi intorno mentre si sposta.
  if math.abs(offset) > 0.01 then
    weaveApplied[index] = true
    physics.setAISplineAbsoluteOffset(index, offset, false)
  elseif weaveApplied[index] then
    weaveApplied[index] = nil
    physics.setAISplineAbsoluteOffset(index, 0, false)
  end

  if c.pit then
    if not pitRequested[index] then
      pitRequested[index] = true
      physics.setAIPitStopRequest(index, true)
      local compound = compoundIndexFor(index, c.compound)
      if compound then physics.setAITyres(index, compound) end
    end
  elseif pitRequested[index] then
    -- La richiesta va anche revocata: prima si dimenticava solo di averla fatta, e la vettura
    -- restava convinta di dover rientrare anche dopo che la strategia aveva cambiato idea.
    pitRequested[index] = nil
    physics.setAIPitStopRequest(index, false)
  end
end

local function applySafetyCar(index, c)
  local idle = c.phase == 'Idle' or c.phase == ''

  if idle then
    if not safetyCarParked then
      -- Fuori dai piedi, in piazzola, finche non serve. Il motore pero resta acceso: prima la
      -- si fermava con lo stallo, e una vettura in stallo potrebbe non ripartire piu quando la
      -- direzione gara la chiama - cioe proprio nel momento in cui serve.
      pcall(function() physics.teleportCarTo(index, ac.SpawnSet.Pits) end)
      physics.setAINoInput(index, true, false)
      physics.setAITopSpeed(index, 1)
      safetyCarParked = true
    end
    return
  end

  if safetyCarParked then
    -- setAINoInput lascia l'IA parcheggiata: svegliarla una sola volta per uscita.
    physics.awakeCar(index)
    physics.setAINoInput(index, false, false)
    physics.setAIStopCounter(index, 0)
    physics.setAIThrottleLimit(index, 1)
    safetyCarParked = false
  end

  -- Il richiamo resta attivo nel motore finche non viene revocato, anche dopo una sosta.
  -- Un secondo intervento o un richiamo annullato deve poter riportare la SC in pista.
  physics.setAIPitStopRequest(index, c.phase == 'Restarting')

  physics.setAILevel(index, 1.0)
  physics.setAIAggression(index, 0)

  if c.phase == 'Deploying' then
    -- Esce dai box e recupera sul gruppo, che nel frattempo e stato rallentato.
    physics.setAITopSpeed(index, (c.topSpeed > 0 and c.topSpeed or 120) + 40)
  elseif c.phase == 'Restarting' then
    physics.setAITopSpeed(index, c.topSpeed > 0 and c.topSpeed or 80)
  else
    physics.setAITopSpeed(index, c.topSpeed > 0 and c.topSpeed or 120)
  end
end

--- Il giro di formazione.
---
--- La ricognizione e gia gestita dai comandi normali, che limitano tutti al ritmo previsto.
--- Qui si fa la parte che Assetto Corsa non sa fare: a fine ricognizione ogni vettura viene
--- riportata nella sua piazzola e tenuta ferma, e al verde vengono liberate tutte insieme.
--- E quello che trasforma un primo giro lento in una partenza da fermo vera.
--- Da quanto stiamo liberando le vetture dopo il verde, in secondi.
local held = {}              -- indice vettura -> vero se la stiamo tenendo ferma noi
local releaseWoken = {}      -- sveglia una sola volta per rilascio, anche con frame lunghi
local parkSkipped = {}       -- vetture non schierate perche non sapevamo dove metterle
local parkSettled = {}       -- vetture gia ferme nella loro piazzola: non si spostano piu
local teleportFailures = 0   -- quante volte il teletrasporto non e riuscito
local teleportError = nil    -- e perche, se lo sappiamo

--- Vero se la vettura e una di quelle che la procedura di partenza deve gestire.
local function formationCar(index)
  if index == safetyCarIndex then return false end   -- la safety car sta ai box, non in griglia
  if index == 0 and state.pitStart then return false end

  -- Una sola condizione: al volante c'e una IA. Vale anche per la vettura del giocatore.
  --
  -- Prima qui c'era un caso speciale per la vettura zero, che la escludeva a meno che non
  -- fosse acceso il pilota automatico di Pitwall. Era sbagliato, e si e visto: se a guidare
  -- e l'IA di Assetto Corsa - l'opzione del gioco per farsi sostituire al volante - il
  -- pilota automatico di Pitwall resta spento, la vettura non veniva messa in piazzola da
  -- nessuno, e la direzione gara la mandava a partire dalla corsia box per non essersi
  -- presentata in griglia. Per una cosa che aveva fatto proprio lei.
  --
  -- Chi guida davvero con le proprie mani ha isAIControlled falso, e continua a mettersi in
  -- piazzola da solo con la piazzola illuminata: quella parte non cambia.
  local car = ac.getCar(index)
  return car ~= nil and car.isAIControlled == true
end

--- Mette una vettura nella sua piazzola e la inchioda li.
---
--- Non basta una chiamata: non esiste in CSP una funzione che congeli una vettura. Il
--- teletrasporto la sposta ma la lascia con la velocita che aveva, e il limite di velocita non
--- frena - agisce sul gas, non sul pedale del freno. Quindi si mette la posizione, si azzera il
--- moto, si toglie l'input e si tiene il freno premuto.
local function parkOnGrid(index)
  local slot = gridSlots[index]

  -- Senza una piazzola catturata non si tocca niente.
  --
  -- Prima qui c'era un ripiego: teletrasporto al punto di partenza. Ma quella mappatura non
  -- la conosciamo - non sappiamo quale punto tocchi a quale vettura, ne come la giri - e il
  -- risultato e stato una vettura messa in griglia rivolta dalla parte sbagliata. Una vettura
  -- lasciata dov'e sta meglio di una messa dove capita: il peggio che succede e che quella
  -- non si schiera, invece di schierarsi al contrario.
  if slot == nil then
    parkSkipped[index] = true
    return false
  end

  local ok, err = pcall(function()
    -- La posizione catturata prima del via, non il punto di spawn: quello e una mappatura che
    -- non conosciamo, e ogni teletrasporto invaliderebbe il giro.
    --
    -- Resta setAICarPosition.
    --
    -- Avevo provato setCarPosition, che secondo la documentazione mette anche l'orientamento e
    -- allinea la vettura alla pista: sembrava la funzione giusta per risolvere le vetture
    -- girate. Il risultato e stato peggiore - girate TUTTE invece di una sola - quindi quella
    -- funzione intende la direzione in un altro modo, e con i dati che abbiamo non e la sua.
    --
    -- La direzione va passata INVERTITA, e non e un trucco: e una misura.
    --
    -- La diagnostica conta quante vetture, dopo essere state schierate, guardano a piu di
    -- novanta gradi dalla direzione che abbiamo chiesto. Il risultato era venti su venti.
    -- Venti su venti non e deriva ne sfortuna: vuol dire che questa funzione intende il
    -- vettore al contrario di come lo espone car.look. Glielo si da girato, e il contatore
    -- deve scendere a zero.
    local verso = vec3(-slot.look.x, 0, -slot.look.z)
    physics.setAICarPosition(index, slot.pos, verso)
    physics.setCarVelocity(index, vec3())
    physics.setWheelAngularVelocity(index, ac.Wheel.All, 0)
  end)

  if not ok then
    teleportFailures = teleportFailures + 1
    teleportError = tostring(err)
  end
  return ok
end

--- Tiene ferma una vettura in piazzola. Da richiamare a ogni fotogramma.
local function holdOnGrid(index)
  pcall(function()
    -- Niente setAINoInput.
    --
    -- Quella funzione mette la vettura in "parked state", e da quello stato non si esce
    -- passandole false: le vetture restavano inchiodate al verde alzando i giri del motore
    -- senza muoversi. Ho eliminato una per una le altre cause - orientamento sbagliato, freno,
    -- frizione staccata - e quando restava solo questa ho smesso di difenderla.
    --
    -- Per tenere ferma una vettura bastano il freno e il freno a mano, che sono comandi senza
    -- memoria: si applicano finche servono e smettono di esistere appena si smette di
    -- scriverli. Nessuno stato da cui tirarle fuori.

    -- Un'IA ferma che non riceve questa riga viene considerata in panne e si ritira: dopo
    -- qualche secondo in griglia il gruppo cominciava a sparire.
    physics.preventAIFromRetiring(index)

    -- Il freno si puo forzare: fra il valore dell'IA e il nostro vince il massimo. E quando
    -- il semaforo e acceso non si sta solo fermi: si sale di giri, come fanno davvero.
    if state.lights > 0 then
      -- I giri salgono con le luci: una luce poco, cinque luci quasi a fondo.
      controlsLaunch(index, 0.35 + 0.1 * math.min(5, state.lights))
    else
      controlsHold(index)
    end

    -- Ma il freno da solo non basta: con il motore che spinge le vetture strisciavano in avanti
    -- e si presentavano al semaforo gia oltre la piazzola. Qui si toglie anche la spinta.
    --
    -- Tutte e tre reversibili, e questo e il punto: il limite di velocita, il taglio del gas e
    -- il conto di frenata dell'IA si disfano scrivendoci sopra il valore normale. Il parked
    -- state che c'era prima no, e le vetture restavano inchiodate al verde.
    physics.setAITopSpeed(index, 1)
    physics.setAIThrottleLimit(index, 0)

    -- "Fai frenare l'IA per un tempo": si rinnova a ogni fotogramma e si annulla con zero.
    physics.setAIStopCounter(index, 0.5)
  end)
end

local function applyFormation(dt)
  local phase = state.fPhase
  local sim = ac.getSim()
  local holding = connected and sim ~= nil and sim.raceSessionType == ac.SessionType.Race
    and (phase == 'Forming' or phase == 'Lights')
  local total = sim ~= nil and sim.carsCount or 0

  if holding then
    for i = 0, total - 1 do
      if formationCar(i) then
        local car = ac.getCar(i)
        local placed = parkSettled[i] == true

        -- Si riposiziona solo chi non e ancora a posto, e poi non lo si tocca piu.
        --
        -- Il riposizionamento continuo era nato per un motivo giusto: le IA arrivano in
        -- piazzola ancora lanciate e una spinta sola non basta a fermarle. Ma azzerare la
        -- velocita non azzera la ROTAZIONE, e non esiste una funzione per azzerarla. Rimettere
        -- la vettura al suo posto sessanta volte al secondo mentre continua a girare su se
        -- stessa vuol dire una vettura che dopo qualche secondo guarda dalla parte sbagliata.
        -- Da li non riparte, il rilascio al verde fallisce, e la gara comincia con tutti fermi
        -- in griglia e una bandiera gialla che non si spegne piu.
        --
        -- Quindi: si insiste finche e lontana o in movimento, e appena e ferma al suo posto
        -- si smette. Da quel momento la tengono fermi il freno e il taglio dell'input.
        if not parkSettled[i] then
          local fermo = (car.speedKmh or 0) <= 2
          local aPosto = false

          local slot = gridSlots[i]
          if slot ~= nil and car.position ~= nil then
            local dx = car.position.x - slot.pos.x
            local dz = car.position.z - slot.pos.z
            local dot = car.look ~= nil and (car.look.x * slot.look.x + car.look.z * slot.look.z) or -1
            aPosto = (dx * dx + dz * dz) < 2.25 and dot > 0.9
          end

          if fermo and aPosto then
            parkSettled[i] = true
            placed = true
          else
            placed = parkOnGrid(i)
          end
        end

        -- Un teletrasporto fallito o una piazzola sconosciuta non devono fermare l'auto in pista.
        if placed then
          holdOnGrid(i)
          if not held[i] then releaseWoken[i] = nil end
          held[i] = true
        end
      end
    end
    formationHeld = true
    return true
  end

  formationHeld = false
  parkSettled = {}
  return false
end

--- Libera le vetture che stiamo tenendo ferme, e insiste finche non si muovono davvero.
---
--- Sta qui e non dentro l'applicazione dei comandi perche quella ha tre uscite anticipate - non
--- collegato, physics non concessa, procedura in corso - e bastava incrociarne una nei secondi
--- del verde per lasciare meta gruppo inchiodato a un chilometro orario per il resto della gara.
--- La condizione d'uscita non e piu un tempo deciso a tavolino ma la vettura che si muove: e
--- l'unica prova che il rilascio e arrivato a destinazione.
--- Da quanto stiamo provando a liberare le vetture, in secondi.
local releaseTimer = 0

--- Oltre questo tempo si smette di insistere e si lascia perdere.
---
--- Insistere per sempre sembrava prudente ed e stato il contrario. A Melbourne diciannove
--- vetture sono rimaste in questa lista per tutta la gara, e ogni fotogramma ricevevano una
--- chiamata ad awakeCar, che annulla il giro: non solo non ripartivano, ma non potevano piu
--- segnare un tempo. Dopo qualche secondo o il rilascio ha funzionato, o il problema e
--- altrove e continuare fa solo danni.
local RELEASE_GIVE_UP = 8

--- Quante volte abbiamo rinunciato: finisce nella diagnostica, perche e un sintomo.
local releaseGaveUp = 0

--- Com'erano messe le vetture nell'istante in cui abbiamo rinunciato.
---
--- Due numeri che distinguono due difetti diversi, e che da fuori si confondono. Se sono
--- ferme ma orientate bene, e il rilascio a non funzionare. Se guardano dalla parte
--- sbagliata, e la messa in griglia ad averle girate, e il rilascio non c'entra niente.
local releaseStuck = -1
local releaseBackwards = -1

--- Quante vetture, fra quelle che stiamo tenendo, guardano dalla parte opposta alla piazzola.
local function countBackwards()
  local n = 0
  for index in pairs(held) do
    local car = ac.getCar(index)
    local slot = gridSlots[index]
    if car ~= nil and slot ~= nil and car.look ~= nil then
      -- Prodotto scalare fra le due direzioni: negativo vuol dire piu di novanta gradi.
      local dot = car.look.x * slot.look.x + car.look.z * slot.look.z
      if dot < 0 then n = n + 1 end
    end
  end
  return n
end

local function releaseHeldCars(dt)
  if next(held) == nil then releaseTimer = 0; return end
  local sim = ac.getSim()
  local holding = connected and sim ~= nil and sim.raceSessionType == ac.SessionType.Race
    and (state.fPhase == 'Forming' or state.fPhase == 'Lights')

  if holding then releaseTimer = 0 else releaseTimer = releaseTimer + (dt or 0.016) end

  for index in pairs(held) do
    local car = ac.getCar(index)
    if car == nil then
      held[index] = nil
      releaseWoken[index] = nil
    elseif not holding or (index == 0 and state.pitStart) then
      pcall(function()
        -- Una vettura tenuta ferma viene messa a dormire dal motore fisico, e da li non riparte
        -- qualunque comando le si dia. Questa e la riga che la sveglia, ed e il pezzo che manca
        -- a quasi tutte le implementazioni in giro. Ma annulla anche il giro in corso, quindi
        -- si chiama una volta per vettura. Una soglia temporale salta del tutto il risveglio
        -- se il primo fotogramma dopo il verde dura piu di mezzo secondo.
        if not releaseWoken[index] then
          physics.awakeCar(index)
          releaseWoken[index] = true
        end

        physics.setAINoInput(index, false, false)
        physics.setAIStopCounter(index, 0)
        physics.setAITopSpeed(index, 1e9)
        physics.setAIThrottleLimit(index, 1)

        -- E si molla tutto quello che le tenevamo addosso: freno, freno a mano, e soprattutto
        -- la frizione, che senza questa riga restava staccata per sempre.
        controlsNeutral(index)
      end)
      if (car.speedKmh or 0) > 8 or (index == 0 and state.pitStart) then
        held[index] = nil
        releaseWoken[index] = nil
      end
    end
  end

  -- Si rinuncia. Le vetture restano dove sono, ma almeno tornano a essere vetture normali:
  -- nessun freno forzato, nessun giro annullato, e i comandi di gara tornano a comandarle.
  if releaseTimer > RELEASE_GIVE_UP and next(held) ~= nil then
    releaseBackwards = countBackwards()
    releaseStuck = 0
    for index in pairs(held) do
      local car = ac.getCar(index)
      if car ~= nil and (car.speedKmh or 0) <= 8 then releaseStuck = releaseStuck + 1 end
    end

    for index in pairs(held) do
      pcall(function()
        physics.setAINoInput(index, false, false)
        controlsNeutral(index)
      end)
    end
    held = {}
    releaseWoken = {}
    releaseTimer = 0
    releaseGaveUp = releaseGaveUp + 1
  end
end

local function applyCommands(dt)
  appliedCount = 0

  -- Scollegati si restituisce il DRS a tutti. Il permesso resta appiccicato alla vettura: se
  -- ci si ferma con qualcuno a cui era stato negato, quello resterebbe senza ala per tutta la
  -- sessione senza che nessuno glielo possa piu ridare.
  if not connected then releaseAllDrs(); return end
  if not physicsAllowed() then return end

  local sim = ac.getSim()
  if sim == nil then return end
  if sim.carsCount ~= (indexByName.__count or -1) then
    rebuildIndices()
    indexByName.__count = sim.carsCount
  end

  local sessionType = sim.raceSessionType
  local racing = sessionType == ac.SessionType.Race
  acIsRace = racing
  acIsPractice = sessionType == ac.SessionType.Practice
                 or sessionType == ac.SessionType.Qualify
                 or sessionType == ac.SessionType.Hotlap

  -- I distacchi servono anche al DRS, non solo alla ricognizione: il secondo al punto di
  -- rilevamento si misura da qui.
  if state.fPhase == 'Rolling' or state.drsGap > 0 then measureGaps() end
  refreshDrsWrites(dt)
  updateDrsGrants()
  applyPlayerDrs()

  -- Consumo e rifornimenti. Questo passaggio sta prima della procedura di partenza di
  -- proposito: quella esce subito, e si porterebbe via anche il rifornimento, che in gara si
  -- fa proprio mentre le vetture sono ferme in piazzola ad aspettare il semaforo.
  for name, c in pairs(commands) do
    local index = indexByName[nameKey(name)]
    if type(index) == 'number' and not c.isSafetyCar then
      pcall(function()
        learnFuel(index)
        resetFuelHold(index, ac.getCar(index))
        applyFuel(index, c, racing)
        applyTyres(index, c)
      end)
    end
  end

  -- Durante la griglia e il semaforo comanda solo la procedura di partenza.
  if applyFormation(dt) then return end

  unmatched = {}
  myLapKind = ''
  playerCommanded = false
  for name, c in pairs(commands) do
    local index = indexByName[nameKey(name)]
    if index == nil and c.isSafetyCar then index = safetyCarIndex end

    if index == nil then
      -- Un nome che non trova la sua vettura prima spariva in silenzio. Adesso finisce nella
      -- diagnostica: e l'unico modo per accorgersi che meta gruppo non riceve comandi.
      unmatched[#unmatched + 1] = name
    elseif type(index) == 'number' then
      local car = ac.getCar(index)
      if car and car.isAIControlled then
        -- Un pcall per vettura, non uno per tutto il ciclo: prima un errore su una qualsiasi
        -- interrompeva il resto, e siccome l'ordine di pairs non e stabilito le vetture servite
        -- cambiavano da gara a gara.
        local ok, err = pcall(function()
          if c.isSafetyCar then applySafetyCar(index, c) else applyRacer(index, c) end
        end)
        if ok then
          appliedCount = appliedCount + 1
          if index == 0 then
            myLapKind = c.lapKind or ''
            playerCommanded = true
          end
        else physicsNotice = tostring(err) end
      end
    end
  end

  -- Chi guida con le proprie mani non riceve comandi, ed e giusto cosi. Ma se al volante c e
  -- l IA del gioco la vettura e a tutti gli effetti un iscritta come le altre, e deve
  -- rispettare il ritmo della ricognizione e delle neutralizzazioni insieme a loro.
  if not playerCommanded then pcall(paceCarUnderGameAi, 0) end
end

local systemTicks, windowTicks = 0, 0

local function tick(dt)
  if dt and dt > 0 then lastDt = math.max(0.001, math.min(0.1, dt)) end
  refresh(dt)
  local ok, err = pcall(applyCommands, dt)
  if not ok then physicsNotice = tostring(err) end
end

--- L'aggiornamento di sistema non viene chiamato in tutte le configurazioni, quindi la lettura
--- parte anche dal disegno della finestra. La soglia di tempo dentro refresh evita i doppioni.
--- Il tempo lo porta solo l'aggiornamento di sistema.
---
--- Questa funzione viene chiamata da due parti - dall'aggiornamento di sistema e dalla finestra -
--- perche in certe configurazioni la prima non viene invocata. Ma quando la finestra e aperta
--- girano entrambe, e prima ogni conteggio basato sul tempo avanzava al doppio: una finestra di
--- tre secondi ne durava uno e mezzo, e un dato considerato vecchio dopo quattro lo diventava
--- dopo due. La chiamata dalla finestra ora serve solo a garantire la lettura, senza portare
--- tempo con se.
local function safeTick(dt, fromWindow)
  if fromWindow then
    windowTicks = windowTicks + 1
    if systemTicks > 0 then dt = 0 end
  else
    systemTicks = systemTicks + 1
  end
  pcall(tick, dt)
end

-- ---------------------------------------------------------------------------------------------
-- La piazzola illuminata
--
-- Un rettangolo luminoso sull'asfalto, disegnato dove ti tocca stare. Verde quando sei dentro e
-- fermo, ambra quando ci sei quasi, bianco quando sei lontano, con una linea che indica la
-- strada. Serve perche in Formula 1 la piazzola e dipinta per terra e si vede: qui no, e senza
-- un riferimento mettersi al posto giusto sarebbe indovinare.
-- ---------------------------------------------------------------------------------------------

--- Diagnosi scritta su disco dall'app, non dal controller.
---
--- Il controller meteo gira in un contesto piu chiuso e il disco potrebbe non essergli
--- concesso: se non scrive, non vuol dire che non funzioni, e la sua assenza non prova niente.
--- L'app invece i file li scrive di sicuro - lo fa gia per i comandi - e puo leggere lo stato
--- del controller dal canale condiviso. Cosi una sola riga dice come stanno davvero le cose.
local diagTimer = 99

local function writeDiagnostics(dt)
  diagTimer = diagTimer + dt
  if diagTimer < 3 then return end
  diagTimer = 0

  local ok, base = pcall(ac.getFolder, ac.FolderID.Documents)
  if not ok or base == nil then return end

  local beat = tonumber(ac.load('.pitwall.wfx.beat')) or -1
  local lines = {
    'app_viva=1',
    'app_collegata=' .. (connected and '1' or '0'),
    'app_seq=' .. tostring(state.seq),
    'physics=' .. tostring(physicsOk),
    'punti_meteo_ricevuti=' .. #weatherPoints,
    'fase_formazione=' .. tostring(state.fPhase),
    'luci_accese=' .. tostring(state.lights),
    'vetture_bloccate=' .. tostring(formationHeld),
    'vetture_da_liberare=' .. (function() local n=0 for _ in pairs(held) do n=n+1 end return n end)(),
    'comandi_applicati=' .. appliedCount,
    'in_attesa_ai_box=' .. (function() local n=0 for _ in pairs(pitHeld) do n=n+1 end return n end)(),
    'richiesti_fermi_dal_core=' .. (function()
        local n = 0
        for _, c in pairs(commands) do if c.holdInPit then n = n + 1 end end
        return n
      end)(),
    'minuto_sessione=' .. string.format('%.2f', weatherMinute),
    'sessione_secondo_ac=' .. (acIsRace and 'gara'
                               or (acIsPractice and 'prove o qualifica' or 'non ancora nota')),
    'sessione_secondo_pitwall=' .. (state.qSegment ~= '' and ('qualifica ' .. state.qSegment)
                                    or (state.fPhase ~= 'Idle' and 'gara' or 'non dichiarata')),
    'rilasci_falliti=' .. releaseGaveUp,
    'alla_rinuncia_ferme=' .. releaseStuck,
    'alla_rinuncia_girate=' .. releaseBackwards,
    'girate_adesso=' .. countBackwards(),
    'piazzole_sconosciute=' .. (function() local n=0 for _ in pairs(parkSkipped) do n=n+1 end return n end)(),
    'zone_drs=' .. #(drsZones or {}),
    'drs_concesso=' .. tostring(state.drsEnabled) .. ' ' .. (state.drsNotice or '-'),
    'drs_distacco_richiesto=' .. string.format('%.2f', state.drsGap or 0),
    'drs_negato_a=' .. (function()
        local n = 0
        for _, written in pairs(drsWritten) do if written == false then n = n + 1 end end
        return n
      end)(),
    'benzina_al_giro=' .. (function()
        local mine = fuelPerLap[0]
        if mine == nil then return 'non ancora misurata' end
        return string.format('%.2f l su %d giri', mine, fuelSamples[0] or 0)
      end)(),
    'benzina_imbarcata=' .. (function()
        local n = 0
        for _ in pairs(fuelSet) do n = n + 1 end
        return n .. ' vetture'
      end)(),
    'nomi_senza_vettura=' .. (#unmatched > 0 and table.concat(unmatched, ', ') or '(nessuno)'),
    'teletrasporti_falliti=' .. teleportFailures,
    'errore_teletrasporto=' .. tostring(teleportError or '-'),
    'minuto_meteo=' .. string.format('%.1f', weatherMinute),
    '',
    'controller_caricato=' .. tostring(ac.load('.pitwall.wfx.loaded') or 0),
    'controller_inizializzato=' .. tostring(ac.load('.pitwall.wfx.init') or 0),
    'controller_battito=' .. beat,
    'controller_vivo=' .. (beat >= 0 and '1' or '0 (nessun battito: CSP non lo sta eseguendo)'),
    'controller_origine=' .. tostring(ac.load('.pitwall.wfx.source') or '-'),
    'controller_punti=' .. tostring(ac.load('.pitwall.wfx.points') or 0),
    'controller_pioggia=' .. tostring(ac.load('.pitwall.wfx.rain') or 0),
    'controller_errore=' .. tostring(ac.load('.pitwall.wfx.error') or '-'),
    '',
    'pioggia_vera_sim=' .. string.format('%.3f', ac.getSim().rainIntensity or 0),
    'bagnato_vero_sim=' .. string.format('%.3f', ac.getSim().rainWetness or 0)
  }
  pcall(function()
    io.save(tostring(base) .. '\\Pitwall\\live\\diagnostica.txt', table.concat(lines, '\n') .. '\n')
  end)
end

--- Scrive una riga di comando per il plugin. Stesso canale dei pulsanti del volante.
local function sendCommand(line)
  local ok, base = pcall(ac.getFolder, ac.FolderID.ACDocuments)
  if not ok or base == nil then return end
  pcall(function() io.save(tostring(base) .. '\\Pitwall\\command.txt', line .. '\n') end)
end

local GRID_HALF_LENGTH = 3.0
local GRID_HALF_WIDTH = 1.3
local glow = 0

local function drawGridSlot()
  if state.fPhase ~= 'Forming' and state.fPhase ~= 'Lights' then return end
  local slot = gridSlots[0]
  if slot == nil or not gridState.known then return end

  local right = vec3(-slot.look.z, 0, slot.look.x)
  local lift = vec3(0, 0.03, 0)   -- appena sopra l'asfalto, o sparisce dentro la pista

  local colour
  if gridState.inside then
    colour = rgbm(0.1, 1.0, 0.3, 0.30 + glow * 0.18)
  elseif gridState.distance < 8 then
    colour = rgbm(1.0, 0.70, 0.1, 0.26 + glow * 0.16)
  else
    colour = rgbm(0.9, 0.9, 0.95, 0.20 + glow * 0.12)
  end

  local f = slot.look * GRID_HALF_LENGTH
  local r = right * GRID_HALF_WIDTH
  local c = slot.pos + lift

  render.quad(c - f - r, c - f + r, c + f + r, c + f - r, colour)

  -- Il bordo, piu acceso del riempimento: da lontano si legge la forma, non la macchia.
  local edge = rgbm(colour.r, colour.g, colour.b, math.min(1, colour.mult * 3))
  render.debugLine(c - f - r, c - f + r, edge)
  render.debugLine(c + f - r, c + f + r, edge)
  render.debugLine(c - f - r, c + f - r, edge)
  render.debugLine(c - f + r, c + f + r, edge)

  -- Da lontano, la strada per tornarci.
  local car = ac.getCar(0)
  if car ~= nil and gridState.distance > 4 then
    render.debugLine(vec3(car.position.x, car.position.y + 0.4, car.position.z),
                     c + vec3(0, 0.4, 0), rgbm(1, 0.8, 0.2, 0.8))
  end
end

render.on('main.track.transparent', function() pcall(drawGridSlot) end)

-- Quanto spesso riferire alla direzione gara dove siamo: ogni frame riscriverebbe un file
-- centinaia di volte al secondo per niente.
local gridReportTimer = 0
local gridReported = nil

local function reportGrid(dt)
  if state.fPhase ~= 'Forming' then gridReported = nil return end
  if not gridState.known then return end

  gridReportTimer = gridReportTimer + dt
  local changed = gridReported ~= gridState.inside
  if not changed and gridReportTimer < 0.5 then return end

  gridReportTimer = 0
  gridReported = gridState.inside
  sendCommand(string.format('report-grid %s %.1f', gridState.inside and '1' or '0', gridState.distance))
end

-- ---------------------------------------------------------------------------------------------
-- Il pilota automatico
--
-- Serve per collaudare il campionato senza stare al volante: safety car, bandiere, strategia e
-- giro di formazione si guardano meglio da fermi che mentre si guida. Acceso, la vettura passa
-- all'IA del gioco e da quel momento riceve gli stessi comandi di tutte le altre - limiti sotto
-- neutralizzazione, soste, mescole - perche il motore la mette nella lista come un avversario.
-- ---------------------------------------------------------------------------------------------

local autopilotOn = nil

--- Quanto manca al prossimo tentativo di uscire dal garage, in secondi.
local startRetry = 0

--- Quanti tentativi restano. Zero vuol dire: non si prova piu.
---
--- Il limite non e una precauzione teorica, e una correzione. Senza, si ritentava una volta al
--- secondo per sempre: se il giocatore premeva Drive da solo, ac.tryToStart rispondeva "gia
--- partita" - cioe falso - e il tentativo non finiva mai. Per tutto il fine settimana. La CPU
--- e salita oltre il 99% e si e dovuto spegnere il simulatore.
---
--- Dieci tentativi in dieci secondi sono piu che sufficienti: se in dieci secondi il pulsante
--- Drive non c era, o non c e, o qualcuno lo ha gia premuto.
local startTries = 10

local function followAutopilot(dt)
  local want = state.autopilot == true and connected

  -- Chiedere all'IA di guidare non basta: bisogna anche essere usciti dal garage.
  --
  -- setCarAutopilot da la vettura all'IA, ma una vettura che non e ancora in pista non c'e, e
  -- l'IA non ha niente da guidare: si resta nel menu dei box, con il mondo che gira intorno e
  -- la propria vettura ferma nel garage. Da fuori sembra che il pilota automatico sia rotto.
  --
  -- ac.tryToStart preme il pulsante Drive di quel menu. Si ritenta una volta al secondo perche
  -- nei primi istanti di una sessione il pulsante non c'e ancora, e il primo tentativo cade a
  -- vuoto senza dire niente: restituisce falso e basta.
  if want and startTries > 0 then
    -- Se la vettura e gia in pista non c e niente da premere, e si smette subito: e il caso di
    -- chi ha premuto Drive da solo, che e anche il piu frequente.
    local car = ac.getCar(0)
    if car ~= nil and car.isInPit ~= true then
      startTries = 0
    else
      startRetry = startRetry - (dt or 0)
      if startRetry <= 0 then
        startRetry = 1
        startTries = startTries - 1
        local ok, partita = pcall(function() return ac.tryToStart(true) end)
        if ok and partita == true then startTries = 0 end
      end
    end
  elseif not want then
    startTries = 10
    startRetry = 0
  end

  if want == autopilotOn then return end
  autopilotOn = want

  -- Il secondo argomento lascia scegliere le gomme all'IA: senza, partirebbe con quelle montate
  -- anche sotto il diluvio.
  pcall(function() physics.setCarAutopilot(want, true) end)
end

--- Partenza dalla corsia box.
---
--- Chi non ha preso la sua piazzola in tempo viene portato nel suo garage e trattenuto li
--- finche il gruppo non e sfilato, poi lasciato andare. E la regola vera: non si viene
--- squalificati, si parte dai box.
---
--- Il teletrasporto si fa una volta sola - ripeterlo rimetterebbe la vettura in garage a ogni
--- fotogramma, e non si uscirebbe mai - ma il blocco si riscrive sempre, perche un solo
--- fotogramma saltato basterebbe a partire in mezzo al gruppo.
local pitStartDone = false
local pitStartTimer = 0
local pitStartBlocked = false
local flagShown = nil

local function releasePitStarter()
  if not pitStartBlocked then return end
  local ok = pcall(function()
    physics.awakeCar(0)
    physics.setCarNoInput(false)
    physics.overrideRacingFlag(ac.FlagType.None)
  end)
  if ok then
    pitStartBlocked = false
    flagShown = nil
  end
end

local function applyPitLaneStart(dt)
  local sim = ac.getSim()
  if not connected or sim == nil or sim.raceSessionType ~= ac.SessionType.Race or not state.pitStart then
    releasePitStarter()
    pitStartDone = false
    pitStartTimer = 0
    return
  end
  if sim.isPaused or sim.isReplayActive then return end

  if not pitStartDone then
    pitStartDone = true
    pitStartTimer = 0
    pcall(function()
      physics.teleportCarTo(0, ac.SpawnSet.Pits)
      pitStartBlocked = true
      physics.setCarVelocity(0, vec3())
      physics.setWheelAngularVelocity(0, ac.Wheel.All, 0)
    end)
  end

  -- Il conto parte dal verde, non dal teletrasporto: finche la gara non e lanciata non ha
  -- senso contare i secondi di attesa.
  local launched = state.status == 'Green' or state.fPhase == 'Away'
  if launched then pitStartTimer = pitStartTimer + (dt or 0.016) end

  if pitStartTimer < state.pitStartDelay or not launched then
    pcall(function()
      physics.setCarNoInput(true)
      pitStartBlocked = true
      physics.overrideRacingFlag(ac.FlagType.ReturnToPits)
    end)
  else
    releasePitStarter()
  end
end

--- Tiene la safety car ai box quando non serve, in qualunque sessione.
---
--- Sta qui e non insieme agli altri comandi perche quelli il motore li manda solo in gara: in
--- prove libere e in qualifica non arriva niente, e l'IA di Assetto Corsa si metteva a girare
--- con la safety car come se fosse un'iscritta qualsiasi. In pista deve esserci solo quando la
--- direzione gara la chiama.
local parkedTimer = 0

local function keepSafetyCarParked(dt)
  if safetyCarIndex == nil then
    -- I nomi si rileggono qui perche in prove libere l'applicazione dei comandi, che di solito
    -- se ne occupa, esce prima di arrivarci.
    parkedTimer = parkedTimer + dt
    if parkedTimer > 2 then
      parkedTimer = 0
      pcall(rebuildIndices)
    end
    return
  end

  local deployed = state.scPhase ~= nil and state.scPhase ~= '' and state.scPhase ~= 'Idle'
  if deployed then return end          -- se la direzione gara l'ha chiamata, comanda lei

  local car = ac.getCar(safetyCarIndex)
  if car == nil then return end

  if not safetyCarParked then
    safetyCarParked = true
    pcall(function() physics.teleportCarTo(safetyCarIndex, ac.SpawnSet.Pits) end)
  end

  pcall(function()
    physics.setAINoInput(safetyCarIndex, true, false)
    physics.setAITopSpeed(safetyCarIndex, 1)
    -- Una vettura ferma che non riceve questa chiamata viene considerata in panne e si ritira:
    -- la safety car sparirebbe dall'elenco prima ancora di servire.
    physics.preventAIFromRetiring(safetyCarIndex)
  end)
end

--- La bandiera ufficiale del gioco, cosi la vedono anche l'IA e gli altri cruscotti, non solo
--- il nostro. Si scrive solo quando cambia: queste impostazioni restano, non vanno ripetute.
local function showRacingFlag()
  if state.pitStart and (not pitStartDone or pitStartBlocked) then return end

  local want = ac.FlagType.None
  local s = state.status
  if s == 'SafetyCar' or s == 'SafetyCarEnding' or s == 'VirtualSafetyCar'
     or s == 'FormationLap' or s == 'LocalYellow' then
    want = ac.FlagType.Caution
  end

  if want == flagShown then return end
  flagShown = want
  pcall(function() physics.overrideRacingFlag(want) end)
end

-- Una gara riavviata puo passare direttamente da Away a Rolling: aspettare Idle lascia
-- la fotografia della gara precedente. L'evento del simulatore copre anche questo caso.
ac.onSessionStart(function()
  if physicsAllowed() then
    for index in pairs(held) do
      pcall(function()
        controlsNeutral(index)
        physics.setAIStopCounter(index, 0)
        physics.setAITopSpeed(index, 1e9)
        physics.setAIThrottleLimit(index, 1)
      end)
    end
    releasePitStarter()
  end
  gridSlots = {}
  gridCaptured = false
  gridState.known = false
  held = {}
  releaseWoken = {}
  parkSettled = {}
  parkSkipped = {}
  formationHeld = false
  releaseTimer = 0
  rollingTimer = 0
  gridReported = nil
  gridReportTimer = 0
  pitStartDone = false
  pitStartTimer = 0
  state.fPhase = 'Idle'
  state.pitStart = false
  state.lights = 0
  formationAwaitingStart = true
end)

function script.update(dt)
  if physicsRetry > 0 then physicsRetry = physicsRetry - dt end
  if physicsAllowed() then
    pcall(followAutopilot, dt)
    pcall(releaseHeldCars, dt)
    pcall(keepSafetyCarParked, dt)
    pcall(showRacingFlag)
    pcall(applyPitLaneStart, dt)
  end

  -- La fase della serpentina la conta questo script, invece di derivarla dall'orologio del
  -- gioco: cosi il periodo e quello scritto qui a prescindere dall'unita di misura di sim.time,
  -- che non avevo mai verificato e che se fosse in secondi avrebbe dato un ciclo di 44 minuti,
  -- cioe uno scostamento fisso e nessuna serpentina.
  weavePhase = weavePhase + dt * WEAVE_RATE

  -- Il cronometro della ricognizione: riparte da zero ogni volta che la fase cambia.
  if state.fPhase == 'Rolling' then rollingTimer = rollingTimer + dt else rollingTimer = 0 end
  warmupClock = warmupClock + dt

  -- La serpentina sale e scende piano, invece di accendersi e spegnersi di colpo.
  local target = state.fPhase == 'Rolling' and 1 or 0
  local step = dt / WEAVE_FADE_SECONDS
  if weaveRamp < target then weaveRamp = math.min(target, weaveRamp + step)
  elseif weaveRamp > target then weaveRamp = math.max(target, weaveRamp - step) end

  -- Finita la ricognizione gli scostamenti vanno rimessi a zero qui, e non insieme agli altri
  -- comandi: appena si forma la griglia i comandi IA smettono di essere applicati, e quello che
  -- resta scritto se lo porterebbero dietro in gara, partendo tutte fuori traiettoria.
  if weaveRamp <= 0.01 and next(weaveApplied) ~= nil then
    pcall(function()
      for index, applied in pairs(weaveApplied) do
        if applied then physics.setAISplineAbsoluteOffset(index, 0, false) end
      end
    end)
    weaveApplied = {}
  end

  glow = (math.sin(ac.getSim().time / 260) + 1) / 2
  pcall(writeDiagnostics, dt)
  pcall(captureGrid)
  pcall(measureGrid)
  pcall(reportGrid, dt)
  safeTick(dt, false)
end

-- ---------------------------------------------------------------------------------------------
-- Interfaccia
-- ---------------------------------------------------------------------------------------------

local COLORS = {
  Green = rgbm(0.2, 0.85, 0.3, 1),
  LocalYellow = rgbm(1, 0.85, 0.1, 1),
  VirtualSafetyCar = rgbm(1, 0.75, 0.1, 1),
  SafetyCar = rgbm(1, 0.6, 0.05, 1),
  SafetyCarEnding = rgbm(1, 0.6, 0.05, 1),
  RedFlag = rgbm(0.95, 0.15, 0.15, 1),
  FormationLap = rgbm(0.55, 0.75, 1, 1),
  Checkered = rgbm(0.9, 0.9, 0.9, 1),
  Inactive = rgbm(0.5, 0.5, 0.5, 1)
}

local function statusColor() return COLORS[state.status] or COLORS.Inactive end

local function drawDeltaBar()
  if state.status ~= 'VirtualSafetyCar' and state.status ~= 'SafetyCar' then return end

  local width = ui.availableSpaceX()
  local height = 18
  local cursor = ui.getCursor()
  local compliant = state.delta >= 0

  ui.drawRectFilled(cursor, cursor + vec2(width, height), rgbm(0.12, 0.12, 0.12, 0.9))

  -- Riempie dal centro: a destra quando si rispetta il delta, a sinistra quando si va troppo
  -- forte, come il riferimento dei giochi ufficiali.
  local centre = cursor.x + width / 2
  local clamped = math.max(-3, math.min(3, state.delta))
  local extent = (clamped / 3) * (width / 2)
  local color = compliant and rgbm(0.2, 0.8, 0.35, 1) or rgbm(0.95, 0.2, 0.2, 1)

  if extent >= 0 then
    ui.drawRectFilled(vec2(centre, cursor.y), vec2(centre + extent, cursor.y + height), color)
  else
    ui.drawRectFilled(vec2(centre + extent, cursor.y), vec2(centre, cursor.y + height), color)
  end

  ui.drawLine(vec2(centre, cursor.y), vec2(centre, cursor.y + height), rgbm(1, 1, 1, 0.6), 1)
  ui.dummy(vec2(width, height + 2))

  ui.textColored(string.format(T('ac.delta.s'), state.delta), color)
  if not compliant then
    ui.sameLine()
    ui.textColored(T('ac.rallenta'), rgbm(0.95, 0.2, 0.2, 1))
  end
end

function windowMain(dt)
  safeTick(dt or 0.016, true)

  if not connected then
    ui.textColored(T('ac.pitwall.non.collegato'), rgbm(0.8, 0.5, 0.2, 1))
    ui.text(T('ac.serve.simhub.aperto.con.il'))
    ui.textColored(string.format(T('ac.cicli.sistema.finestra'), systemTicks, windowTicks),
                   rgbm(0.55, 0.55, 0.55, 1))
    for _, p in ipairs(attempts) do
      ui.pushTextWrapPosition(0)
      ui.textColored(p, rgbm(0.6, 0.6, 0.6, 1))
      ui.popTextWrapPosition()
    end
    return
  end

  ui.pushFont(ui.Font.Title)
  ui.textColored(state.text, statusColor())
  ui.popFont()

  ui.text(string.format(T('ac.giro.p'), state.lap, state.position))

  -- Deve vedersi sempre: sapere chi ha il volante in mano e la prima cosa.
  if state.autopilot then
    ui.textColored(T('ac.pilota.automatico'), rgbm(0.35, 0.8, 1, 1))
  end

  -- Sorpasso in ricognizione: finche il conto scende si puo ancora rimediare, ed e giusto che
  -- si veda quanto tempo resta invece di scoprire la penalita a cose fatte.
  if state.giveBack > 0 then
    ui.pushFont(ui.Font.Title)
    ui.textColored(string.format(T('ac.restituisci.la.posizione.sei'), state.giveBack),
                   rgbm(1, 0.45, 0.1, 1))
    ui.popFont()
    if state.giveBackLeft > 0 then
      ui.textColored(string.format(T('ac.secondi'), state.giveBackLeft), rgbm(1, 0.7, 0.3, 1))
    end
  end

  if state.fPhase == 'Forming' then
    if gridState.known and not gridState.inside then
      -- Lungo o corto, dentro o fuori: due numeri dicono come correggere, la distanza da sola no.
      local along = gridState.along
      local across = gridState.across
      ui.textColored(string.format(T('ac.piazzola.m'), state.gridSlot, gridState.distance),
                     COLORS.FormationLap)
      ui.textColored(string.format(T('ac.m.m'),
                     along > 0 and 'indietro' or 'avanti', math.abs(along),
                     across > 0 and 'a sinistra' or 'a destra', math.abs(across)),
                     rgbm(0.8, 0.8, 0.85, 1))
    elseif gridState.known then
      ui.textColored(T('ac.in.piazzola.resta.fermo'), rgbm(0.2, 0.95, 0.35, 1))
    else
      ui.textColored(T('ac.fermati.nella.tua.piazzola'), COLORS.FormationLap)
    end
    if state.gridNotice ~= '' then
      ui.textColored(state.gridNotice, rgbm(0.65, 0.65, 0.7, 1))
    end
  elseif state.fPhase == 'Lights' then
    ui.pushFont(ui.Font.Title)
    ui.textColored(T('ac.semaforo'), rgbm(0.95, 0.2, 0.2, 1))
    ui.popFont()
  end

  if #state.yellow > 0 then
    ui.textColored(T('ac.giallo.settore') .. table.concat(state.yellow, ', '), COLORS.LocalYellow)
  end
  if state.pitClosed then
    ui.textColored(T('ac.pit.lane.chiusa'), rgbm(0.95, 0.3, 0.3, 1))
  end

  drawDeltaBar()

  if state.penalties > 0 then
    ui.textColored(string.format(T('ac.penalita.s'), state.penalties), rgbm(0.95, 0.3, 0.3, 1))
  end

  if state.message ~= '' then
    ui.separator()
    ui.pushTextWrapPosition(0)
    ui.textColored(state.message, rgbm(0.85, 0.9, 1, 1))
    ui.popTextWrapPosition()
  end

  ui.separator()
  if physicsNotice then
    ui.pushTextWrapPosition(0)
    ui.textColored(physicsNotice, rgbm(0.8, 0.6, 0.3, 1))
    ui.popTextWrapPosition()
  elseif appliedCount > 0 then
    local scText = safetyCarParked and 'ai box' or state.scPhase
    ui.textColored(string.format(T('ac.ia.pilotata.vetture.safety.car'), appliedCount, scText),
                   rgbm(0.6, 0.8, 1, 1))
    if myLapKind ~= '' then
      ui.textColored(T('ac.giro') .. myLapKind, rgbm(0.6, 0.8, 1, 1))
    end

    local waiting = 0
    for _ in pairs(pitHeld) do waiting = waiting + 1 end
    if waiting > 0 then
      ui.textColored(string.format(T('ac.in.attesa.ai.box'), waiting), rgbm(0.6, 0.8, 1, 1))
    end
  else
    ui.textColored(T('ac.ia.non.ancora.agganciata'), rgbm(0.6, 0.6, 0.6, 1))
  end
end

-- ---------------------------------------------------------------------------------------------
-- HUD di gara
--
-- La finestra Pitwall e il pannello tecnico della direzione gara: serve a capire cosa sta
-- succedendo, non a correre. Questo invece e quello che si guarda mentre si corre, e segue le
-- regole di un grafico televisivo: pochi elementi, grandi, e ognuno occupa sempre lo stesso
-- posto, cosi si legge con la coda dell'occhio invece di doverlo cercare.
-- ---------------------------------------------------------------------------------------------

local HUD = {
  green = rgbm(0.15, 0.85, 0.3, 1),
  yellow = rgbm(1, 0.85, 0.1, 1),
  orange = rgbm(1, 0.6, 0.05, 1),
  red = rgbm(0.95, 0.15, 0.15, 1),
  white = rgbm(0.95, 0.96, 1, 1),
  dim = rgbm(0.55, 0.58, 0.65, 1),
  panel = rgbm(0.05, 0.06, 0.08, 0.72),
  player = rgbm(1, 0.82, 0.2, 1)
}

--- Il semaforo di partenza: cinque luci che si accendono una per volta e si spengono insieme.
--- E l'unico momento in cui un HUD puo permettersi di occupare il centro dello schermo.
local function drawLights(width)
  local lit = state.lights or 0
  local showing = state.fPhase == 'Lights' or (state.fPhase == 'Away' and lit > 0)
  if not showing and state.fPhase ~= 'Forming' then return false end

  local count = 5
  local size = math.min(42, width / (count * 1.6))
  local gap = size * 0.55
  local total = count * size + (count - 1) * gap
  local cursor = ui.getCursor() + vec2((width - total) / 2, 0)

  for i = 1, count do
    local centre = cursor + vec2((i - 1) * (size + gap) + size / 2, size / 2)
    local on = i <= lit
    -- Spente restano visibili ma scure: il semaforo si vede anche prima che si accenda, ed e
    -- cosi che si capisce quante luci mancano.
    ui.drawCircleFilled(centre, size / 2, on and rgbm(0.9, 0.08, 0.08, 1) or rgbm(0.12, 0.05, 0.05, 0.9), 24)
    ui.drawCircle(centre, size / 2, on and rgbm(1, 0.5, 0.5, 1) or rgbm(0.3, 0.3, 0.32, 0.8), 24, 2)
    if on then ui.drawCircleFilled(centre, size / 2 + 4, rgbm(0.9, 0.1, 0.1, 0.18), 24) end
  end

  ui.dummy(vec2(width, size + 6))
  return true
end

--- Le qualifiche a eliminazione: manche, tempo che resta, quanti passano.
local function drawQualifying(width)
  if state.qSegment == nil or state.qSegment == '' then return end

  local cursor = ui.getCursor()
  local h = 28
  ui.drawRectFilled(cursor, cursor + vec2(width, h), rgbm(0.10, 0.07, 0.07, 0.9))
  ui.drawRectFilled(cursor, cursor + vec2(5, h), HUD.red)

  ui.dummy(vec2(0, 4))
  ui.sameLine(14)
  ui.pushFont(ui.Font.Title)
  ui.textColored(state.qSegment, HUD.white)
  ui.popFont()

  ui.sameLine(72)
  local left = math.max(0, state.qLeft or 0)
  ui.textColored(string.format('%d:%02d', math.floor(left / 60), left % 60),
                 left < 120 and HUD.orange or HUD.white)

  if (state.qSurviving or 0) > 0 and state.qSegment ~= 'Q3' then
    ui.sameLine(150)
    ui.textColored(string.format(T('ac.passano.in'), state.qSurviving), HUD.dim)
  end

  ui.dummy(vec2(width, h - 16))
end

--- La fascia di stato: una riga che dice sempre di che colore e la gara adesso.
local function drawStatus(width)
  local colour, text = HUD.green, state.text

  if state.status == 'SafetyCar' or state.status == 'SafetyCarEnding' then colour = HUD.orange
  elseif state.status == 'VirtualSafetyCar' then colour = HUD.orange
  elseif state.status == 'LocalYellow' then colour = HUD.yellow
  elseif state.status == 'RedFlag' then colour = HUD.red
  elseif state.status == 'FormationLap' then colour = rgbm(0.55, 0.75, 1, 1)
  elseif state.status == 'Checkered' then colour = HUD.white
  end

  local cursor = ui.getCursor()
  local h = 30
  ui.drawRectFilled(cursor, cursor + vec2(width, h), rgbm(colour.r * 0.3, colour.g * 0.3, colour.b * 0.3, 0.85))
  ui.drawRectFilled(cursor, cursor + vec2(6, h), colour)
  ui.dummy(vec2(0, 6))
  ui.sameLine(14)
  ui.pushFont(ui.Font.Title)
  ui.textColored(text ~= '' and text or '-', colour)
  ui.popFont()
  ui.dummy(vec2(width, h - 18))
end

--- Il DRS, come in televisione.
---
--- Due informazioni sole: se la direzione gara lo ha abilitato, e - quando non lo ha fatto -
--- perche. Fuori dalla gara non si disegna niente, perche in zona il DRS e sempre libero e
--- una scritta fissa sarebbe solo rumore.
local function drawDrs(width)
  if state.drsGap <= 0 and state.drsEnabled then return end

  local car = ac.getCar(0)

  -- Se la direzione gara non lo concede, non lo concede: e inutile guardare altro.
  --
  -- Prima qui si leggeva car.drsAvailable, che e la bandierina di Assetto Corsa: quella dice
  -- solo "sei dentro una zona DRS", perche il gioco di regole non ne applica nessuna. Cosi
  -- durante il giro di formazione il pannello scriveva DRS DISPONIBILE in verde mentre la
  -- direzione gara lo stava negando a tutti. La verdetto e il nostro.
  local colour, text

  if not state.drsEnabled then
    colour = HUD.dim
    text = state.drsNotice ~= '' and state.drsNotice or 'DRS NON DISPONIBILE'
  elseif gapRuleActive() and drsGrant[0] ~= true then
    colour, text = HUD.dim, 'DRS: TROPPO DISTANTE'
  elseif car ~= nil and car.drsActive == true then
    colour, text = HUD.green, 'DRS APERTO'
  elseif car ~= nil and car.drsAvailable == true then
    colour, text = HUD.green, 'DRS DISPONIBILE'
  else
    colour, text = HUD.white, 'DRS ABILITATO'
  end

  local cursor = ui.getCursor()
  local h = 20
  ui.drawRectFilled(cursor, cursor + vec2(width, h), rgbm(0.08, 0.08, 0.10, 0.85))
  ui.drawRectFilled(cursor, cursor + vec2(4, h), colour)
  ui.dummy(vec2(0, 2))
  ui.sameLine(12)
  ui.textColored(text, colour)
  ui.dummy(vec2(width, h - 16))
end

--- La classifica. Solo quello che serve a capire la gara: chi, quanto dista, con che gomma.
local function drawStandings(width)
  if #standings == 0 then
    ui.textColored(T('ac.classifica.non.disponibile'), HUD.dim)
    return
  end

  for i = 1, #standings do
    local r = standings[i]
    local colour = r.player and HUD.player or HUD.white

    -- La riga rossa delle qualifiche: chi e gia fuori resta spento, chi ci finirebbe adesso e
    -- acceso di rosso. In televisione si guarda piu quella dei tempi.
    if r.eliminated then colour = rgbm(0.42, 0.44, 0.48, 1)
    elseif r.dropZone then colour = HUD.red end

    ui.textColored(string.format('%2d', r.position), r.player and HUD.player or HUD.dim)
    ui.sameLine(34)
    ui.textColored(r.name ~= '' and r.name or '---', colour)
    ui.sameLine(96)
    ui.textColored(r.gap ~= '' and r.gap or '-', r.player and HUD.player or HUD.dim)
    ui.sameLine(168)
    if r.pit then
      ui.textColored(T('ac.box'), HUD.orange)
    else
      ui.textColored(r.compound ~= '' and r.compound or '-', colour)
      if r.age > 0 then
        ui.sameLine(196)
        ui.textColored(tostring(r.age), r.age > 25 and HUD.orange or HUD.dim)
      end
    end
  end
end

function windowHud(dt)
  if not connected then
    ui.textColored(T('ac.pitwall.non.collegato'), HUD.dim)
    return
  end

  local width = ui.availableSpaceX()

  local lights = drawLights(width)
  if lights then ui.dummy(vec2(1, 4)) end

  if state.pitStart then
    ui.pushFont(ui.Font.Title)
    ui.textColored(T('ac.partenza.dai.box'), HUD.orange)
    ui.popFont()
    ui.pushTextWrapPosition(0)
    ui.textColored(T('ac.non.hai.preso.la.tua'), HUD.white)
    ui.popTextWrapPosition()

    local left = math.max(0, state.pitStartDelay - pitStartTimer)
    if left > 0.1 then
      ui.textColored(string.format(T('ac.trattenuto.ancora.s'), left), HUD.orange)
    else
      ui.textColored(T('ac.puoi.uscire.rispetta.il.limite'), HUD.good)
    end
    return
  end

  drawQualifying(width)
  drawStatus(width)
  drawDrs(width)

  -- Quello che la direzione gara ti sta chiedendo adesso, se c'e qualcosa.
  if state.giveBack > 0 then
    ui.textColored(string.format(T('ac.restituisci.la.posizione.s'), state.giveBackLeft), HUD.orange)
  elseif state.fPhase == 'Forming' and state.gridNotice ~= '' then
    ui.textColored(state.gridNotice, HUD.yellow)
  elseif state.targetKmh > 0 then
    ui.textColored(string.format(T('ac.massimo.km.h.delta.s'), state.targetKmh, state.delta),
                   state.delta < -1 and HUD.orange or HUD.dim)
  end

  if state.penalties > 0 then
    ui.textColored(string.format(T('ac.penalita.s.2'), state.penalties), HUD.red)
  end
  if state.autopilot then
    ui.textColored(T('ac.pilota.automatico'), rgbm(0.35, 0.8, 1, 1))
  end

  ui.dummy(vec2(1, 6))
  ui.textColored(string.format(T('ac.giro.2'), state.lap), HUD.dim)
end
