#!/bin/bash

# Użycie: ./runsc.sh 524 [-b] [-show]

CODE="$1"
BUILD_FLAG=""
SHOW_FLAG=0

shift
while [[ $# -gt 0 ]]; do
  case "$1" in
    -b)
      BUILD_FLAG="-b"
      ;;
    -show)
      SHOW_FLAG=1
      ;;
  esac
  shift
done

if [[ -z "$CODE" ]]; then
  echo "Użycie: $0 <code> [-b] [-show]"
  exit 1
fi

cd CryptoBlade || { echo "Brak katalogu /CryptoBlade"; exit 1; }

git reset --hard HEAD
git clean -fd
git pull
git checkout gemini

cd CryptoBlade/Scripts || { echo "Brak katalogu /CryptoBlade/Scripts"; exit 1; }

# Zapisz timestamp przed uruchomieniem dockera
START_TS=$(date +%s)

# Uruchomienie PowerShell i skryptu z parametrem (z opcjonalną flagą -b)
pwsh ./RunStrategyContainer.ps1 -Code "$CODE" -vm $BUILD_FLAG

# Po wyjściu z PowerShella: znajdź ostatni uruchomiony kontener i pokaż logi
container=$(docker ps --latest --format "{{.Names}}")
if [[ -n "$container" ]]; then
  echo "Ostatni uruchomiony kontener: $container"
  docker logs "$container" -f
else
  echo "Nie znaleziono uruchomionych kontenerów."
fi

# Jeśli podano -show, wyświetl result.json z najnowszego folderu utworzonego po starcie dockera
if [[ $SHOW_FLAG -eq 1 ]]; then
  # Szukaj we wszystkich strategiach
  RESULTS_ROOT="CryptoBlade/CryptoBlade/Data/Strategies"
  find "$RESULTS_ROOT" -type d -path "*/Backtest/Results/*" | while read -r folder; do
    # Sprawdź datę utworzenia folderu (ctime)
    FOLDER_TS=$(stat -c %Y "$folder")
    if (( FOLDER_TS > START_TS )); then
      RESULT_FILE="$folder/result.json"
      if [[ -f "$RESULT_FILE" ]]; then
        echo "Zawartość $RESULT_FILE:"
        cat "$RESULT_FILE"
      fi
    fi
  done
fi