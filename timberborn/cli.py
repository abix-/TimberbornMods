"""Interactive REPL for co-piloting Timberborn."""
import shlex
import sys
import threading
import time

import requests

from timberborn.api import TimberbornAPI

HELP_TEXT = """
Commands:
  status              show all levers + adapters
  on <name>           switch lever on
  off <name>          switch lever off
  color <name> <hex>  set lever color (e.g. color Lever%201 ff0000)
  watch [secs]        poll adapters every N seconds (default 5)
  stop                stop polling
  ping                check if game API is reachable
  help                show this message
  quit / exit         exit
"""


class Watcher:
    """Background thread that polls adapters and prints changes."""

    def __init__(self, api, interval=5.0):
        self.api = api
        self.interval = interval
        self._stop = threading.Event()
        self._thread = None
        self._prev_states = {}

    def start(self):
        if self._thread and self._thread.is_alive():
            print("[watch] already running")
            return
        self._stop.clear()
        self._thread = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()
        print(f"[watch] polling every {self.interval}s -- type 'stop' to end")

    def stop(self):
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=2)
        print("[watch] stopped")

    def _loop(self):
        while not self._stop.is_set():
            try:
                adapters = self.api.get_adapters()
                for a in adapters:
                    name = a.get("name", "?")
                    state = a.get("state")
                    prev = self._prev_states.get(name)
                    if prev is not None and prev != state:
                        label = "ON" if state else "OFF"
                        prev_label = "ON" if prev else "OFF"
                        print(f"\n[CHANGE] {name}: {prev_label} -> {label}")
                    self._prev_states[name] = state
            except requests.ConnectionError:
                print("\n[watch] game not reachable -- retrying...")
            except Exception as exc:
                print(f"\n[watch] error: {exc}")
            self._stop.wait(self.interval)


def print_status(api):
    """Print all levers and adapters."""
    try:
        levers = api.get_levers()
    except requests.ConnectionError:
        print("  (game not reachable)")
        return
    except Exception as exc:
        print(f"  error: {exc}")
        return

    print(f"\n  Levers ({len(levers)}):")
    if not levers:
        print("    (none)")
    for lev in levers:
        state = "ON" if lev.get("state") else "OFF"
        spring = " [spring]" if lev.get("springReturn") else ""
        print(f"    {lev.get('name', '?'):30s} {state}{spring}")

    try:
        adapters = api.get_adapters()
    except Exception as exc:
        print(f"\n  Adapters: error: {exc}")
        return

    print(f"\n  Adapters ({len(adapters)}):")
    if not adapters:
        print("    (none)")
    for ad in adapters:
        state = "ON" if ad.get("state") else "OFF"
        print(f"    {ad.get('name', '?'):30s} {state}")
    print()


def main():
    api = TimberbornAPI()
    watcher = Watcher(api)

    print("=== Timberborn Co-Pilot ===")
    if api.ping():
        print("Connected to game at localhost:8080")
    else:
        print("Game not detected at localhost:8080 -- start Timberborn first")
        print("(commands will retry when you issue them)\n")

    print("Type 'help' for commands.\n")

    while True:
        try:
            line = input("tb> ").strip()
        except (EOFError, KeyboardInterrupt):
            break

        if not line:
            continue

        try:
            parts = shlex.split(line)
        except ValueError:
            parts = line.split()

        cmd = parts[0].lower()
        args = parts[1:]

        try:
            if cmd in ("quit", "exit", "q"):
                break
            elif cmd == "help":
                print(HELP_TEXT)
            elif cmd == "ping":
                if api.ping():
                    print("  connected")
                else:
                    print("  not reachable")
            elif cmd == "status":
                print_status(api)
            elif cmd == "on":
                if not args:
                    print("  usage: on <lever-name>")
                else:
                    name = " ".join(args)
                    api.switch_on(name)
                    print(f"  {name} -> ON")
            elif cmd == "off":
                if not args:
                    print("  usage: off <lever-name>")
                else:
                    name = " ".join(args)
                    api.switch_off(name)
                    print(f"  {name} -> OFF")
            elif cmd == "color":
                if len(args) < 2:
                    print("  usage: color <lever-name> <hex>")
                else:
                    hex_val = args[-1]
                    name = " ".join(args[:-1])
                    api.set_color(name, hex_val)
                    print(f"  {name} -> color {hex_val}")
            elif cmd == "watch":
                interval = float(args[0]) if args else 5.0
                watcher.interval = interval
                watcher.start()
            elif cmd == "stop":
                watcher.stop()
            else:
                print(f"  unknown command: {cmd} (type 'help')")
        except requests.ConnectionError:
            print("  game not reachable -- is Timberborn running?")
        except requests.HTTPError as exc:
            print(f"  API error: {exc.response.status_code} {exc.response.text}")
        except Exception as exc:
            print(f"  error: {exc}")

    watcher.stop()
    print("bye!")


if __name__ == "__main__":
    main()
