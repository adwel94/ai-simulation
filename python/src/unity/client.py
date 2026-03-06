import requests


class UnitySimClient:
    """REST API client for the Unity claw machine simulation server."""

    def __init__(self, base_url: str = "http://localhost:8765", timeout: float = 30.0):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout

    def status(self) -> dict:
        """GET /status — check server health."""
        resp = requests.get(f"{self.base_url}/status", timeout=self.timeout)
        resp.raise_for_status()
        return resp.json()

    def capture(self) -> dict:
        """GET /capture — capture screenshot without executing any action."""
        resp = requests.get(f"{self.base_url}/capture", timeout=self.timeout)
        resp.raise_for_status()
        return resp.json()

    def reset(self) -> dict:
        """POST /reset — start a new episode, returns initial observation."""
        resp = requests.post(f"{self.base_url}/reset", timeout=self.timeout)
        resp.raise_for_status()
        return resp.json()

    def step(self, action: dict) -> dict:
        """POST /step — execute an action, returns new observation.

        Action format:
            {"type": "move", "direction": "left", "duration": 0.3, "reasoning": "..."}
            {"type": "lower", "duration": 0.5}
            {"type": "raise", "duration": 0.5}
            {"type": "grip", "state": "close"}
            {"type": "camera", "direction": "left", "angle": 45}
            {"type": "done", "reasoning": "..."}
        """
        resp = requests.post(
            f"{self.base_url}/step",
            json=action,
            timeout=self.timeout,
        )
        resp.raise_for_status()
        return resp.json()
