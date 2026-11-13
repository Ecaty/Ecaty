"""Exercise real REST traffic while the built Unity player's Update loop runs."""
import http.client
import json
import pathlib
import subprocess
import time
import urllib.parse

ROOT=pathlib.Path(__file__).resolve().parents[1]
report={"passed":False,"checks":[]}
log=ROOT/"Verification/player-rest.log"

def request(method,path):
    conn=http.client.HTTPConnection("127.0.0.1",27750,timeout=4)
    try:
        conn.request(method,"/"+path)
        response=conn.getresponse()
        data=response.read().decode()
        assert response.status==200,(response.status,data)
        return json.loads(data) if data else None
    finally: conn.close()

# Refuse to collide with the user's running Flecs instance.
try:
    request("GET","entity/flecs/core/World")
except (ConnectionRefusedError,TimeoutError,OSError): pass
else: raise RuntimeError("Port 27750 is already occupied; stop that instance before this test")

proc=subprocess.Popen([str(next(p for p in (ROOT/"Build").glob("*.exe") if not p.name.startswith("UnityCrashHandler"))),"-batchmode","-nographics","-logFile",str(log)])
try:
    deadline=time.monotonic()+30
    while True:
        if proc.poll() is not None: raise RuntimeError("Player exited during startup")
        try:
            request("GET","entity/scene/Cube?values=true&type_info=true")
            break
        except (ConnectionRefusedError,TimeoutError,OSError):
            if time.monotonic()>deadline: raise
            time.sleep(0.2)
    report["checks"].append("built Unity player serves native Flecs REST while Update runs")
    request("PUT","entity/PlayerRemote")
    code="Remote { Position: {3, 1, 0}; Primitive: {1}; Color: {0,1,0,1} }"
    request("PUT","script/PlayerRemote?code="+urllib.parse.quote(code,safe=""))
    response=request("GET","entity/Remote?values=true")
    assert "Position" in json.dumps(response)
    request("GET","query?expr="+urllib.parse.quote("Position, Primitive",safe=""))
    report["checks"].append("remote Script creates a live entity; queries and reflection work")
    code="Remote { Position: {-3, 2, 0}; Primitive: {2} }"
    request("PUT","script/PlayerRemote?code="+urllib.parse.quote(code,safe=""))
    request("DELETE","entity/Remote")
    request("GET","stats/world")
    time.sleep(0.25)
    assert proc.poll() is None,"Player crashed during remote mutation"
    text=log.read_text(errors="replace")
    assert "Exception:" not in text and "fatal:" not in text,text[-2000:]
    report["checks"].append("runtime edit/delete and statistics; no player exceptions")
    report["passed"]=True
finally:
    if proc.poll() is None:
        proc.terminate()
        proc.wait(timeout=10)
    (ROOT/"Verification/player-rest-checks.json").write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
