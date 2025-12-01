import os

class Settings:
    def __init__(self) -> None:
        self.role: str = os.getenv("ROLE", "leader")  # "leader" or "follower"

        # only used on leader
        self.followers_raw: str = os.getenv("FOLLOWERS", "")
        self.write_quorum: int = int(os.getenv("WRITE_QUORUM", "1"))
        self.min_delay_ms: int = int(os.getenv("MIN_DELAY_MS", "0"))
        self.max_delay_ms: int = int(os.getenv("MAX_DELAY_MS", "0"))

        # only used on follower
        self.leader_url: str = os.getenv("LEADER_URL", "")

    @property
    def followers(self) -> list[str]:
        if not self.followers_raw:
            return []
        return [f.strip() for f in self.followers_raw.split(",") if f.strip()]


settings = Settings()
