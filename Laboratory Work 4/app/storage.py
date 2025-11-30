from typing import Dict, Optional

class KeyValueStore:
    def __init__(self) -> None:
        # for the lab an in-memory dict is enough
        self._data: Dict[str, str] = {}

    def get(self, key: str) -> Optional[str]:
        return self._data.get(key)

    def put(self, key: str, value: str) -> None:
        self._data[key] = value

    def snapshot(self) -> Dict[str, str]:
        # used in tests to compare leader vs followers
        return dict(self._data)
