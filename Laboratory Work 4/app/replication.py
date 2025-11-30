import asyncio
import random
from typing import Iterable, Optional

import httpx

from .config import settings


class Replicator:
    """
    Handles sending writes from the leader to all followers.

    Semi-synchronous:
      - sends replication requests concurrently
      - waits for 'write_quorum' successful acks
      - returns immediately once quorum is reached (remaining replications continue in background)
    """

    def __init__(self, followers: Iterable[str]) -> None:
        self.followers = list(followers)
        self.write_quorum = settings.write_quorum
        self.min_delay_ms = settings.min_delay_ms
        self.max_delay_ms = settings.max_delay_ms
        # Shared client for all replications (better connection reuse)
        self._client: Optional[httpx.AsyncClient] = None

    async def _get_client(self) -> httpx.AsyncClient:
        """Get or create the shared HTTP client"""
        if self._client is None or self._client.is_closed:
            self._client = httpx.AsyncClient(timeout=30.0)
        return self._client

    async def _replicate_to_one(
        self,
        follower: str,
        key: str,
        value: str,
    ) -> bool:
        """Replicate a single key-value to one follower"""
        # simulate network lag
        delay_sec = random.uniform(self.min_delay_ms / 1000, self.max_delay_ms / 1000)
        await asyncio.sleep(delay_sec)

        # internal endpoint on follower
        client = await self._get_client()
        url = f"http://{follower}/internal/replicate/{key}"
        try:
            resp = await client.put(url, json={"value": value})
            resp.raise_for_status()
            return True
        except Exception:
            return False

    async def replicate(self, key: str, value: str) -> bool:
        """
        Returns True if write_quorum followers acknowledged,
        False otherwise.
        
        IMPORTANT: Returns as soon as quorum is reached!
        Remaining replications continue in the background.
        """
        if not self.followers or self.write_quorum <= 0:
            # degenerate case: no followers, or no quorum needed
            return True

        # Create tasks for all followers
        tasks = [
            asyncio.create_task(self._replicate_to_one(follower, key, value))
            for follower in self.followers
        ]

        acks = 0
        
        # as_completed lets us handle tasks as they finish, not in order
        for coro in asyncio.as_completed(tasks):
            try:
                result = await coro
                if result:
                    acks += 1
                # Return immediately once quorum is reached!
                if acks >= self.write_quorum:
                    # Don't wait for remaining tasks - they'll complete in background
                    # This is the key for semi-sync: return fast once quorum is met
                    return True
            except Exception:
                # follower failed -> ignore, just don't count it
                pass

        # If we get here, we've processed all tasks but didn't reach quorum
        return False
