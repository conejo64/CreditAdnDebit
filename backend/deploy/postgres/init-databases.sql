SELECT 'CREATE DATABASE cardvault'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'cardvault')\gexec

SELECT 'CREATE DATABASE isoswitch'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'isoswitch')\gexec
