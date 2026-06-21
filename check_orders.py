import sqlite3

try:
    conn = sqlite3.connect('ColdChainX.Infrastructure/coldchainx.db')
    cursor = conn.cursor()
    cursor.execute("SELECT Count(*) FROM transport_orders WHERE Status = 'IN_WAREHOUSE'")
    print(f"Orders IN_WAREHOUSE: {cursor.fetchone()[0]}")
    conn.close()
except Exception as e:
    print(e)
