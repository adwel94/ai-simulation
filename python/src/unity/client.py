import logging
import time

import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry

logger = logging.getLogger(__name__)


class UnitySimClient:
    """REST API client for the Unity claw machine simulation server."""

    def __init__(self, base_url: str = "http://localhost:8765", timeout: float = 30.0):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout

        retry = Retry(
            total=3,
            backoff_factor=1,  # 1s -> 2s -> 4s
            status_forcelist=[502, 503],
            allowed_methods=["GET", "POST"],
        )
        self._session = requests.Session()
        self._session.mount("http://", HTTPAdapter(max_retries=retry))
        self._session.mount("https://", HTTPAdapter(max_retries=retry))

    def _check_response(self, method: str, path: str, resp: requests.Response, **extra) -> None:
        """Log and raise on non-OK responses, including server body."""
        if not resp.ok:
            logger.error(
                f"{method} {path} failed [{resp.status_code}]: {resp.text}",
                extra=extra if extra else None,
            )
        resp.raise_for_status()

    def status(self) -> dict:
        """GET /status — check server health."""
        logger.debug("GET /status")
        resp = self._session.get(f"{self.base_url}/status", timeout=10)
        self._check_response("GET", "/status", resp)
        return resp.json()

    def capture(self) -> dict:
        """GET /capture — capture screenshot without executing any action."""
        logger.debug("GET /capture")
        resp = self._session.get(f"{self.base_url}/capture", timeout=10)
        self._check_response("GET", "/capture", resp)
        return resp.json()

    def reset(self) -> dict:
        """POST /reset — start a new episode, returns initial observation."""
        logger.debug("POST /reset")
        resp = self._session.post(f"{self.base_url}/reset", timeout=45)
        self._check_response("POST", "/reset", resp)
        return resp.json()

    def world_state(self) -> dict:
        """GET /world_state — get ball positions, claw position, camera basis vectors."""
        logger.debug("GET /world_state")
        resp = self._session.get(f"{self.base_url}/world_state", timeout=10)
        self._check_response("GET", "/world_state", resp)
        return resp.json()

    def step(self, action: dict) -> dict:
        """POST /step — fire-and-forget action execution. Returns immediately."""
        logger.debug(f"POST /step | action={action}")
        resp = self._session.post(
            f"{self.base_url}/step",
            json=action,
            timeout=5,
        )
        if not resp.ok:
            logger.error(f"POST /step failed [{resp.status_code}]: {resp.text} | action={action}")
        resp.raise_for_status()
        return resp.json()

    def wait_action_complete(self, timeout: float = 10.0, poll_interval: float = 0.05) -> dict:
        """Poll /status until action_in_progress == false."""
        start = time.time()
        while time.time() - start < timeout:
            status = self.status()
            if not status.get("action_in_progress", False):
                return status
            time.sleep(poll_interval)
        elapsed = time.time() - start
        logger.error(f"wait_action_complete timed out after {elapsed:.1f}s (limit={timeout}s)")
        raise TimeoutError(f"Action not completed within {timeout}s")

    def step_and_observe(self, action: dict, settle: float = 0.3) -> dict:
        """step → wait → settle → capture. 기존 blocking step() 대체."""
        self.step(action)
        self.wait_action_complete()
        time.sleep(settle)
        return self.capture()
