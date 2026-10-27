"""Where a caravel build's time goes (27 October 2026, the build speed-up): runs build_caravel.py (or another script) one
top-level statement at a time in one namespace and writes each statement's seconds, slowest first.

    AstoCADCmd.exe -c "exec(open(r'<root>/tools/caravel/profile_build.py').read())"

Env: CARAVEL_ROOT (as the build), PROFILE_SCRIPT (default tools/caravel/build_caravel.py), PROFILE_OUT (default
models/caravel/build-profile.txt).
"""
import ast
import os
import time

_root = os.environ.get("CARAVEL_ROOT", r"C:/VS mods/Hansa")
_script = os.environ.get("PROFILE_SCRIPT", os.path.join(_root, "tools", "caravel", "build_caravel.py"))
_out = os.environ.get("PROFILE_OUT", os.path.join(_root, "models", "caravel", "build-profile.txt"))
_src = open(_script, encoding="utf-8").read()
_lines = _src.splitlines()
_ns = {"__name__": "__main__", "__file__": _script}
_times = []
_t_all = time.time()
for _node in ast.parse(_src).body:
    _code = compile(ast.Module(body=[_node], type_ignores=[]), _script, "exec")
    _t0 = time.time()
    exec(_code, _ns)
    _times.append((time.time() - _t0, _node.lineno, _lines[_node.lineno - 1].strip()[:110]))
    with open(_out, "w", encoding="utf-8") as _f:          # (after each: a crash or a kill still leaves the profile so far)
        _f.write(f"total {time.time() - _t_all:.1f} s\n")
        for _dt, _ln, _txt in sorted(_times, reverse=True):
            if _dt >= 1.0:
                _f.write(f"{_dt:8.1f} s  line {_ln:5d}  {_txt}\n")
