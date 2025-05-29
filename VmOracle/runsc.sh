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

START_TS=$(date +%s)

pwsh ./RunStrategyContainer.ps1 -Code "$CODE" -vm $BUILD_FLAG

container=$(docker ps -a --format "{{.Names}} {{.CreatedAt}}" | sort -rk2 | head -n1 | awk '{print $1}')
if [[ -z "$container" ]]; then
  echo "Nie znaleziono uruchomionych kontenerów."
  exit 1
fi

echo "Monitoruję kontener: $container"

if [[ $SHOW_FLAG -eq 1 ]]; then
  # Pokazuj logi w 10-sekundowych blokach, aż kontener zakończy działanie
  while docker ps --format '{{.Names}}' | grep -q "^$container$"; do
      echo "Pokazuję logi kontenera $container przez 10 sekund..."
      timeout 10s docker logs -f "$container"
      if ! docker ps --format '{{.Names}}' | grep -q "^$container$"; then
          echo "Kontener $container zakończył działanie!"
          break
      fi
  done
else
  # Tylko sprawdzaj co 10s, czy kontener działa
  while docker ps --format '{{.Names}}' | grep -q "^$container$"; do
      echo "Kontener $container nadal działa, czekam 10s..."
      sleep 10
  done
  echo "Kontener $container zakończył działanie!"
fi

# Szukaj result.json w najnowszym folderze utworzonym po starcie dockera
RESULTS_ROOT="../Data/Strategies"
LATEST_RESULT=""
LATEST_RESULT_TS=0

while IFS= read -r -d '' folder; do
  FOLDER_TS=$(stat -c %Y "$folder")
  if (( FOLDER_TS > START_TS )); then
    if (( FOLDER_TS > LATEST_RESULT_TS )); then
      if [[ -f "$folder/result.json" ]]; then
        LATEST_RESULT_TS=$FOLDER_TS
        LATEST_RESULT="$folder/result.json"
      fi
    fi
  fi
done < <(find "$RESULTS_ROOT" -type d -path "*/Backtest/Results/*" -print0)

if [[ -n "$LATEST_RESULT" ]]; then
  echo "Zawartość $LATEST_RESULT:"
  cat "$LATEST_RESULT"
else
  echo "Nie znaleziono pliku result.json utworzonego po starcie kontenera."
fi