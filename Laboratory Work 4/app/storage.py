from typing import Dict, Optional, Tuple
from dataclasses import dataclass


@dataclass
class VersionedValue:
    """A value with its version number"""
    value: str
    version: int


class KeyValueStore:
    """
    In-memory key-value store with versioning.
    
    Versioning prevents race conditions in replication:
    - Each key has a monotonically increasing version number
    - Followers only accept updates with higher version numbers
    - This ensures all replicas converge to the same final value
    """
    
    def __init__(self) -> None:
        self._data: Dict[str, VersionedValue] = {}

    def get(self, key: str) -> Optional[str]:
        """Get the value for a key (without version info)"""
        entry = self._data.get(key)
        return entry.value if entry else None

    def get_versioned(self, key: str) -> Optional[Tuple[str, int]]:
        """Get value and version for a key"""
        entry = self._data.get(key)
        return (entry.value, entry.version) if entry else None

    def put(self, key: str, value: str) -> int:
        """
        Store a value and increment its version.
        Returns the new version number.
        Used by the LEADER to create new versions.
        """
        current = self._data.get(key)
        new_version = (current.version + 1) if current else 1
        self._data[key] = VersionedValue(value=value, version=new_version)
        return new_version

    def put_if_newer(self, key: str, value: str, version: int) -> bool:
        """
        Store a value only if the version is newer than what we have.
        Returns True if the value was stored, False if rejected.
        Used by FOLLOWERS to handle out-of-order replication.
        """
        current = self._data.get(key)
        current_version = current.version if current else 0
        
        if version > current_version:
            self._data[key] = VersionedValue(value=value, version=version)
            return True
        return False  # Reject stale update

    def snapshot(self) -> Dict[str, str]:
        """Get all key-value pairs (without versions) for comparison"""
        return {k: v.value for k, v in self._data.items()}
    
    def snapshot_versioned(self) -> Dict[str, Tuple[str, int]]:
        """Get all key-value pairs with versions"""
        return {k: (v.value, v.version) for k, v in self._data.items()}
