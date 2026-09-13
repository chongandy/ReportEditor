#!/usr/bin/env bash
# Idempotent Cloud Agent bootstrap for the ReportEditor WPF project.
#
# ReportEditor targets net10.0-windows and uses WPF, so the GUI can only *run*
# on Windows. On this Linux agent we install the .NET 10 SDK and enable
# cross-platform Windows targeting so the project can be restored and built.
set -euo pipefail

DOTNET_DIR="$HOME/.dotnet"
DOTNET_CHANNEL="10.0"

install_sdk() {
  if [ -x "$DOTNET_DIR/dotnet" ] && "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL}\."; then
    echo "==> .NET ${DOTNET_CHANNEL} SDK already installed at $DOTNET_DIR"
    return
  fi
  echo "==> Installing .NET ${DOTNET_CHANNEL} SDK into $DOTNET_DIR"
  local script
  script="$(mktemp)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$script"
  chmod +x "$script"
  "$script" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_DIR"
  rm -f "$script"
}

configure_shell() {
  # Make the SDK and the Windows-targeting flag available in every shell the
  # agent opens (tmux terminals source ~/.bashrc). Guarded so re-runs don't
  # append duplicates.
  local marker="# >>> ReportEditor dotnet env >>>"
  if ! grep -qF "$marker" "$HOME/.bashrc" 2>/dev/null; then
    echo "==> Adding dotnet environment to ~/.bashrc"
    cat >> "$HOME/.bashrc" <<'EOF'

# >>> ReportEditor dotnet env >>>
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
# WPF/net*-windows projects need this to restore & build on non-Windows hosts.
export EnableWindowsTargeting=true
# <<< ReportEditor dotnet env <<<
EOF
  fi
}

main() {
  install_sdk
  configure_shell

  export DOTNET_ROOT="$DOTNET_DIR"
  export PATH="$DOTNET_ROOT:$PATH"
  export DOTNET_CLI_TELEMETRY_OPTOUT=1
  export DOTNET_NOLOGO=1
  export EnableWindowsTargeting=true

  cd "$(dirname "$0")/.."
  echo "==> Restoring and building ReportEditor"
  dotnet build ReportEditor.csproj -c Debug

  echo "==> Environment ready. .NET SDK:"
  dotnet --version
}

main "$@"
