"""Regression checks for the actual companion Lua, with CSP physics replaced by a spy.

Requires lupa (LuaJIT 2.1). Run: python tools/sim/formation_lua_checks.py
No game, SimHub, live commands or championship files are used.
The physics spy verifies calls and state transitions, not real vehicle behaviour.
"""
from pathlib import Path
import sys
import unittest
from lupa.luajit21 import LuaRuntime

SOURCE = Path(sys.argv.pop(1)) if len(sys.argv) > 1 and sys.argv[1].endswith('.lua') else (
    Path(__file__).resolve().parents[2] / 'ac-app/pitwall/pitwall.lua')

FIXTURE = r'''
local vector = {}
vector.__index = vector
function vector:normalize()
  local n = math.sqrt(self.x*self.x + self.y*self.y + self.z*self.z)
  if n > 0 then self.x=self.x/n; self.y=self.y/n; self.z=self.z/n end
  return self
end
function vector:dot(v) return self.x*v.x + self.y*v.y + self.z*v.z end
function vec3(x,y,z) return setmetatable({x=x or 0,y=y or 0,z=z or 0},vector) end
function vec2(x,y) return {x=x,y=y} end
function rgbm(...) return {...} end
function rgb(...) return {...} end
script = {}
render = {on=function() end}
ui = {}
sim = {carsCount=2, raceSessionType=3, isSessionStarted=false, trackLengthM=5000}
cars = {}
overrides = {}
calls = {}
sessionCallback = nil
for i=0,1 do
  cars[i] = {speedKmh=0, isAIControlled=true, position=vec3(0,0,i*10),
    look=vec3(0,0,1), racePosition=i+1, splinePosition=0, lapCount=0}
end
ac = {
  SessionType={Race=3,Practice=1,Qualify=2,Hotlap=4}, Wheel={All=4},
  SpawnSet={Pits=1}, FlagType={None=0,ReturnToPits=1,Caution=2},
  getSim=function() return sim end,
  getCar=function(i) return cars[i] end,
  getDriverName=function(i) return 'Driver '..i end,
  storage=function(v) return v end,
  store=function() end,
  onSessionStart=function(fn) sessionCallback=fn end,
  overrideCarControls=function(i)
    overrides[i] = overrides[i] or {}
    return overrides[i]
  end
}
physics = setmetatable({}, {__index=function(t,k)
  local fn=function(...)
    local a={...}; calls[#calls+1]={name=k,args=a}
    if k=='setAICarPosition' then
      if failTeleport then error('teleport rejected') end
      cars[a[1]].position=vec3(a[2].x,a[2].y,a[2].z)
      cars[a[1]].look=vec3(-a[3].x,-a[3].y,-a[3].z)
    elseif k=='setCarVelocity' then cars[a[1]].speedKmh=0 end
  end
  rawset(t,k,fn); return fn
end})
function countCalls(name)
  local n=0; for _,c in ipairs(calls) do if c.name==name then n=n+1 end end; return n
end
'''

EXPORTS = r'''
return {
  state=state, capture=captureGrid, apply=applyFormation, release=releaseHeldCars,
  pitStart=applyPitLaneStart, parse=parse, flag=showRacingFlag,
  slot=function(i) return gridSlots[i] end,
  held=function(i) return held[i] end,
  connect=function(value) connected=value end
}
'''


class FormationLuaChecks(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.execute(FIXTURE)
        self.api = self.lua.execute(SOURCE.read_text(encoding='utf-8-sig') + EXPORTS)
        self.lua.globals().api = self.api
        self.api.connect(True)

    def execute(self, code):
        self.lua.execute(code)

    def test_missing_grid_never_holds_cars(self):
        self.execute("api.state.fPhase='Forming'; api.apply(0.1)")
        self.assertIsNone(self.api.held(1))
        self.assertEqual(self.lua.eval("countCalls('setAIThrottleLimit')"), 0)

    def test_failed_teleport_never_holds_car_on_track(self):
        self.execute("api.capture(); cars[1].position.z=500; failTeleport=true; api.state.fPhase='Forming'; api.apply(0.1)")
        self.assertIsNone(self.api.held(1))

    def test_missed_initial_grid_never_captures_later_stop(self):
        self.execute("cars[1].speedKmh=60; api.capture(); cars[1].speedKmh=0; cars[1].position.z=500; api.capture()")
        self.assertIsNone(self.api.slot(1))

    def test_grid_snapshot_survives_lap(self):
        self.execute("api.capture(); cars[1].speedKmh=60; api.capture(); cars[1].speedKmh=0; cars[1].position.z=500; api.capture()")
        self.assertEqual(self.api.slot(1).pos.z, 10)

    def test_late_bridge_idle_does_not_erase_captured_grid(self):
        self.execute("api.capture(); cars[1].speedKmh=60; api.capture(); api.state.fPhase='Rolling'")
        self.api.parse('fphase=Idle\n')
        self.assertIsNotNone(self.api.slot(1))
        self.assertEqual(self.api.slot(1).pos.z, 10)

    def test_pit_starter_is_not_teleported_back_to_grid(self):
        self.execute("api.capture(); api.state.fPhase='Forming'; api.state.pitStart=true; cars[0].position.z=300; api.apply(0.1)")
        self.assertIsNone(self.api.held(0))
        self.assertEqual(self.lua.eval('cars[0].position.z'), 300)

    def test_practice_ignores_stale_formation_phase(self):
        self.execute("api.capture(); sim.raceSessionType=1; api.state.fPhase='Lights'; api.apply(0.1)")
        self.assertIsNone(self.api.held(1))

    def test_disconnect_releases_held_cars_even_with_stale_lights(self):
        self.execute("api.capture(); api.state.fPhase='Lights'; api.apply(0.1); api.connect(false); api.release(0.1)")
        self.assertEqual(self.lua.eval('overrides[1].brake'), 0)
        self.assertEqual(self.lua.eval('overrides[1].clutch'), 1)

    def test_green_release_wakes_once_even_with_long_frame(self):
        self.execute("api.capture(); api.state.fPhase='Lights'; api.apply(0.1); api.state.fPhase='Away'; api.release(0.6); api.release(0.1)")
        self.assertEqual(self.lua.eval("countCalls('awakeCar')"), 2)  # Once per car.
        self.assertEqual(self.lua.eval('overrides[1].brake'), 0)
        self.assertEqual(self.lua.eval('overrides[1].clutch'), 1)
        self.execute("cars[0].speedKmh=20; cars[1].speedKmh=20; api.release(0.1)")
        self.assertIsNone(self.api.held(1))

    def test_backwards_car_is_not_marked_settled(self):
        self.execute("api.capture(); cars[1].look.z=-1; api.state.fPhase='Forming'; api.apply(0.1)")
        self.assertGreater(self.lua.eval('cars[1].look.z'), 0)

    def test_pit_release_is_one_shot(self):
        self.execute("api.state.pitStart=true; api.state.fPhase='Away'; api.state.status='Green'; for i=1,30 do api.pitStart(1) end")
        self.assertEqual(self.lua.eval("countCalls('awakeCar')"), 1)
        self.assertEqual(self.lua.eval("countCalls('teleportCarTo')"), 1)

    def test_configured_pit_delay_reaches_lua(self):
        self.api.parse('pitstart=1\nfphase=Away\nstatus=Green\npitstartdelay=3\n')
        self.execute('api.pitStart(1); api.pitStart(1)')
        self.assertEqual(self.lua.eval("countCalls('awakeCar')"), 0)
        self.execute('api.pitStart(1)')
        self.assertEqual(self.lua.eval("countCalls('awakeCar')"), 1)

    def test_session_restart_captures_new_grid(self):
        self.execute("api.capture(); cars[1].speedKmh=60; api.capture(); cars[1].speedKmh=0")
        self.assertIsNotNone(self.lua.globals().sessionCallback)
        self.execute("sessionCallback(0,true); cars[1].position.z=25; api.capture()")
        self.assertEqual(self.api.slot(1).pos.z, 25)

    def test_session_restart_rejects_previous_start_commands(self):
        self.execute("api.state.fPhase='Lights'; api.state.pitStart=true; sessionCallback(0,true)")
        self.api.parse('fphase=Lights\npitstart=1\nlights=5\n')
        self.assertEqual(self.api.state.fPhase, 'Idle')
        self.assertFalse(self.api.state.pitStart)
        self.api.parse('fphase=Rolling\npitstart=0\nlights=0\n')
        self.api.parse('fphase=Forming\n')
        self.assertEqual(self.api.state.fPhase, 'Forming')

    def test_zero_pit_delay_releases_immediately(self):
        self.api.parse('pitstart=1\nfphase=Away\nstatus=Green\npitstartdelay=0\n')
        self.execute('api.pitStart(0.1); api.pitStart(0.1)')
        self.assertEqual(self.lua.eval("countCalls('awakeCar')"), 1)

    def test_pit_delay_stops_during_pause(self):
        self.api.parse('pitstart=1\nfphase=Away\nstatus=Green\npitstartdelay=3\n')
        self.execute('api.pitStart(1); sim.isPaused=true; for i=1,30 do api.pitStart(1) end')
        self.assertEqual(self.lua.eval("countCalls('awakeCar')"), 0)
        self.execute('sim.isPaused=false; api.pitStart(1); api.pitStart(1)')
        self.assertEqual(self.lua.eval("countCalls('awakeCar')"), 1)

    def test_pit_starter_sees_racing_flags_after_release(self):
        self.api.parse('pitstart=1\nfphase=Away\nstatus=Green\npitstartdelay=1\n')
        self.execute("api.pitStart(1); api.state.status='SafetyCar'; api.flag()")
        self.assertEqual(self.lua.eval('calls[#calls].args[1]'), 2)

    def test_pit_starter_released_after_disconnect(self):
        self.execute("api.state.pitStart=true; api.state.fPhase='Lights'; api.pitStart(1); api.connect(false); api.pitStart(1); api.pitStart(1)")
        self.assertEqual(self.lua.eval("countCalls('awakeCar')"), 1)


if __name__ == '__main__':
    unittest.main(verbosity=2)
