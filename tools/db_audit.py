import sqlite3
import os
import sys

# Set console output to UTF-8
if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

# Standard cross-platform path resolution for MostaqlK database
local_app_data = os.environ.get("LOCALAPPDATA", os.path.expanduser(r"~\AppData\Local"))
db_path = os.path.join(local_app_data, "MostaqlK", "Data", "mostaqlk.db")

if not os.path.exists(db_path):
    print(f"Error: Database not found at {db_path}")
    exit(1)

conn = sqlite3.connect(db_path)
cursor = conn.cursor()

print("--- Suspicious Proposal Count Text ---")
cursor.execute("SELECT project_id, title, proposal_count, proposal_count_text FROM projects WHERE proposal_count_text LIKE '%<%' OR proposal_count_text LIKE '%\"%' OR proposal_count_text LIKE '%&%' OR proposal_count_text LIKE '%.%' OR proposal_count_text = ''")
rows = cursor.fetchall()
for row in rows:
    print(f"ID: {row[0]} | Title: {row[1]} | Count: {row[2]} | Text: {row[3]}")

print("\n--- All Projects Proposal Data (Sample) ---")
cursor.execute("SELECT project_id, proposal_count, proposal_count_text FROM projects LIMIT 50")
rows = cursor.fetchall()
for row in rows:
    print(f"ID: {row[0]} | Count: {row[1]} | Text: {row[2]}")

conn.close()
