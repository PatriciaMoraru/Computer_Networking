from pathlib import Path
from urllib.parse import unquote

ROOT: Path | None = None

def set_root(root_path):
  global ROOT
  ROOT = Path(root_path).resolve(strict=False)

  return None

def resolve_safe(uri):
  global ROOT
  if ROOT is None:
    return None
  
  path_part = uri.split('?', 1)[0].split('#', 1)[0]
  path_part = unquote(path_part)

   # ensure it looks like a path starting with '/'
  if not path_part.startswith('/'):
      path_part = '/' + path_part

    # Build candidate under root
    # lstrip('/') so joining doesn't treat it as absolute
  candidate = (ROOT / path_part.lstrip('/')).resolve(strict=False)

  try:
        # Python 3.9+: use relative_to to check ancestor relationship
      candidate.relative_to(ROOT)
  except Exception:
        # outside root
      return None

  return candidate
