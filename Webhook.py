from flask import Flask, request, jsonify
import json
import os

app = Flask(__name__)

# Deinen persoenlichen Secret-Key definieren
SECRET_KEY = "MEIN-GEHEIMNIS-123"

# Liste der erlaubten IPs (ohne localhost)
ALLOWED_IPS = [
    "203.0.113.42",   # Beispiel: deine eigene oeffentliche IP
    # hier ggf. weitere (z.B. AWS-/TradingView-IP-Ranges)
]

@app.route('/webhook', methods=['POST'])
def webhook():
    # 1. IP-Check (immer localhost erlauben)
    remote_ip = request.remote_addr
    if remote_ip != "127.0.0.1" and remote_ip not in ALLOWED_IPS:
        app.logger.warning(f"Zugriff verweigert fuer IP: {remote_ip}")
        return jsonify({'error': 'IP not allowed'}), 403

    # 2. Nur JSON akzeptieren
    if not request.is_json:
        return jsonify({'error': 'Invalid content type, expected JSON'}), 400

    data = request.get_json()

    # 3. Secret-Key prüfen
    if data.get('secret') != SECRET_KEY:
        app.logger.warning(f"Ungültiger Secret: {data.get('secret')} von IP {remote_ip}")
        return jsonify({'error': 'Invalid or missing secret'}), 403

    # 4. Ordner + Datei anlegen
    filename = r"C:\Users\robin\OneDrive\Desktop\Pascal\Neuer Ordner\orders.json"
    folder = os.path.dirname(filename)
    try:
        if not os.path.exists(folder):
            os.makedirs(folder)
            app.logger.info(f"Ordner erstellt: {folder}")

        # 5. Daten speichern
        with open(filename, 'w') as f:
            json.dump(data, f)
        app.logger.info(f"Gueltiger Alert empfangen und gespeichert: {data}")
        return jsonify({'status': 'received'}), 200

    except Exception as e:
        app.logger.error(f"Fehler beim Speichern: {e}")
        return jsonify({'error': 'Failed to save file'}), 500

# 6. Alle anderen Routen abfangen
@app.route('/', defaults={'path': ''})
@app.route('/<path:path>')
def catch_all(path):
    return "Not allowed", 404

if __name__ == "__main__":
    # Flask auf Port 80 starten, alle Interfaces
    app.run(host='0.0.0.0', port=80, debug=True)
