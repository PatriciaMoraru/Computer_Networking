from typing import Optional

from fastapi import FastAPI, HTTPException, status
from pydantic import BaseModel

from .config import settings
from .storage import KeyValueStore
from .replication import Replicator

app = FastAPI(title="KV Store with Single-Leader Replication")

store = KeyValueStore()
replicator: Optional[Replicator] = None

if settings.role == "leader":
    replicator = Replicator(settings.followers)


class ValuePayload(BaseModel):
    value: str


class ReplicatePayload(BaseModel):
    """Payload for internal replication (includes version for consistency)"""
    value: str
    version: int


@app.get("/health")
async def health():
    return {"status": "ok", "role": settings.role}


@app.get("/role")
async def get_role():
    return {"role": settings.role}


# -------- Public API (used by clients) --------

@app.get("/kv/{key}")
async def get_value(key: str):
    value = store.get(key)
    if value is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Key not found")
    return {"key": key, "value": value}


@app.put("/kv/{key}")
async def put_value(key: str, payload: ValuePayload):
    """
    Only the leader should accept writes from clients.
    Followers should reject them.
    """
    if settings.role != "leader":
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Only the leader accepts writes",
        )

    # 1) write on leader - returns the new version number
    version = store.put(key, payload.value)

    # 2) replicate to followers (semi-synchronous) with version
    if replicator is not None:
        ok = await replicator.replicate(key, payload.value, version)
        if not ok:
            # you might still consider the write 'durable enough',
            # but for the lab it's fine to surface an error
            raise HTTPException(
                status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                detail="Write quorum not reached",
            )

    return {"status": "ok", "key": key, "value": payload.value, "version": version}


# -------- Internal API (used by leader -> followers) --------

@app.put("/internal/replicate/{key}")
async def internal_replicate(key: str, payload: ReplicatePayload):
    """
    Called by the leader. Followers apply the write locally.
    
    Uses versioning to handle out-of-order delivery:
    - Only accepts updates with higher version numbers
    - Rejects stale updates (prevents race conditions)
    """
    if settings.role != "follower":
        # if leader accidentally calls itself, or misconfig
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="This node is not a follower",
        )

    # Use put_if_newer to handle race conditions
    accepted = store.put_if_newer(key, payload.value, payload.version)
    
    return {
        "status": "replicated" if accepted else "skipped_stale",
        "key": key,
        "version": payload.version,
        "accepted": accepted
    }
