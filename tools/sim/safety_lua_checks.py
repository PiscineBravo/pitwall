"""Safety-car checks against the actual Lua companion, using the CSP spy fixture.

Run with Python + lupa, as for formation_lua_checks.py. Does not control the game.
The optional first argument selects another pitwall.lua (e.g. a baseline copy).
"""
import unittest
from lupa.luajit21 import LuaRuntime
from formation_lua_checks import FIXTURE, SOURCE


class SafetyLuaChecks(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.execute(FIXTURE)
        self.api = self.lua.execute(SOURCE.read_text(encoding='utf-8-sig') + r'''
return {apply=applySafetyCar, parked=keepSafetyCarParked, state=state,
  setIndex=function(i) safetyCarIndex=i end}
''')
        self.lua.globals().api = self.api
        self.lua.execute("api.setIndex(1); api.parked(.1)")

    def test_departure_wakes_only_once(self):
        self.lua.execute("for i=1,20 do api.apply(1,{phase='Deploying',topSpeed=120}) end")
        self.assertEqual(self.lua.eval("countCalls('awakeCar')"), 1)
        self.assertEqual(self.lua.eval("countCalls('setAIThrottleLimit')"), 1)

    def test_release_restores_input_without_stalling(self):
        self.lua.execute("api.apply(1,{phase='Deploying',topSpeed=120})")
        self.assertTrue(self.lua.eval(r'''(function()
          for _,c in ipairs(calls) do
            if c.name=='setAINoInput' and c.args[2]==false and c.args[3]==false then return true end
          end
          return false
        end)()'''))

    def test_second_deployment_revokes_previous_pit_request(self):
        self.lua.execute(r'''
          api.apply(1,{phase='Deploying',topSpeed=120})
          api.apply(1,{phase='Restarting',topSpeed=80})
          api.apply(1,{phase='Idle',topSpeed=1})
          api.apply(1,{phase='Deploying',topSpeed=120})
        ''')
        self.assertEqual(self.lua.eval("countCalls('awakeCar')"), 2)
        self.assertFalse(self.lua.eval(r'''(function()
          local last=nil
          for _,c in ipairs(calls) do if c.name=='setAIPitStopRequest' then last=c.args[2] end end
          return last
        end)()'''))

    def test_cancelled_withdrawal_revokes_pit_request(self):
        self.lua.execute(r'''
          api.apply(1,{phase='Restarting',topSpeed=80})
          api.apply(1,{phase='Bunching',topSpeed=120})
        ''')
        self.assertFalse(self.lua.eval(r'''(function()
          local last=nil
          for _,c in ipairs(calls) do if c.name=='setAIPitStopRequest' then last=c.args[2] end end
          return last
        end)()'''))

    def test_repeated_idle_does_not_teleport_again(self):
        self.lua.execute("for i=1,20 do api.apply(1,{phase='Idle',topSpeed=1}); api.parked(.1) end")
        self.assertEqual(self.lua.eval("countCalls('teleportCarTo')"), 1)
        self.assertEqual(self.lua.eval("countCalls('awakeCar')"), 0)


if __name__ == '__main__':
    unittest.main(verbosity=2)
