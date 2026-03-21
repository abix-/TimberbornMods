"""Thin wrapper over the Timberborn HTTP API."""
import urllib.parse

import requests


class TimberbornAPI:
    """Client for the Timberborn HTTP API (localhost:8080)."""

    def __init__(self, base_url="http://localhost:8080"):
        self.base_url = base_url.rstrip("/")
        self.session = requests.Session()
        self.session.headers["Accept"] = "application/json"

    def _url(self, path):
        return f"{self.base_url}{path}"

    @staticmethod
    def _encode(name):
        return urllib.parse.quote(name, safe="")

    def _get(self, path):
        resp = self.session.get(self._url(path), timeout=5)
        resp.raise_for_status()
        return resp

    def _post(self, path):
        resp = self.session.post(self._url(path), timeout=5)
        resp.raise_for_status()
        return resp

    # -- connection check --

    def ping(self):
        """Return True if the game API is reachable."""
        try:
            self._get("/api/levers")
            return True
        except (requests.ConnectionError, requests.Timeout):
            return False

    # -- levers (read + write) --

    def get_levers(self):
        """Return list of all levers."""
        return self._get("/api/levers").json()

    def get_lever(self, name):
        """Return a single lever by name."""
        return self._get(f"/api/levers/{self._encode(name)}").json()

    def switch_on(self, name):
        """Turn a lever ON. Returns True on success."""
        self._post(f"/api/switch-on/{self._encode(name)}")
        return True

    def switch_off(self, name):
        """Turn a lever OFF. Returns True on success."""
        self._post(f"/api/switch-off/{self._encode(name)}")
        return True

    def set_color(self, name, hex_color):
        """Set lever color (6-char hex, no #). Returns True on success."""
        color = hex_color.lstrip("#")
        self._post(f"/api/color/{self._encode(name)}/{color}")
        return True

    # -- adapters (read-only) --

    def get_adapters(self):
        """Return list of all adapters."""
        return self._get("/api/adaptors").json()

    def get_adapter(self, name):
        """Return a single adapter by name."""
        return self._get(f"/api/adaptors/{self._encode(name)}").json()
