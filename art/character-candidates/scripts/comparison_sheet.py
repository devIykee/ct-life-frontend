"""Create source-model comparison images with macOS AppKit (no Python image dependencies)."""
import subprocess
from pathlib import Path
subprocess.run(["swift",str(Path(__file__).with_suffix(".swift"))],check=True)
