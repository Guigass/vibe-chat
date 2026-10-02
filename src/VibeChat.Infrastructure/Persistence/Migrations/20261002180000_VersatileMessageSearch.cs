using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeChat.Infrastructure;

#nullable disable

namespace VibeChat.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// B-188: accent-insensitive Portuguese FTS, prefix/websearch tsquery, phrase boost.
    /// Reuses the existing GIN index on messaging.messages.search_vector.
    /// pg_trgm is not added: channel, person and attachment matches run on the
    /// membership-bounded set, and a trigram index would be extra write cost
    /// without a measured gain on that plan.
    /// </summary>
    [DbContext(typeof(VibeChatDbContext))]
    [Migration("20261002180000_VersatileMessageSearch")]
    public partial class VersatileMessageSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS unaccent;

                DO $$
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM pg_ts_config WHERE cfgname = 'portuguese_unaccent'
                  ) THEN
                    CREATE TEXT SEARCH CONFIGURATION public.portuguese_unaccent (COPY = pg_catalog.portuguese);
                    ALTER TEXT SEARCH CONFIGURATION public.portuguese_unaccent
                      ALTER MAPPING FOR word, hword, hword_part
                      WITH unaccent, portuguese_stem;
                    ALTER TEXT SEARCH CONFIGURATION public.portuguese_unaccent
                      ALTER MAPPING FOR numword, hword_numpart, numhword
                      WITH unaccent, simple;
                  END IF;
                END
                $$;

                CREATE OR REPLACE FUNCTION messaging.prefix_tsquery(cfg regconfig, raw text)
                RETURNS tsquery
                LANGUAGE plpgsql
                STABLE
                AS $fn$
                DECLARE
                  base tsquery;
                  rewritten text;
                BEGIN
                  IF raw IS NULL OR btrim(raw) = '' THEN
                    RETURN NULL;
                  END IF;
                  base := websearch_to_tsquery(cfg, raw);
                  IF base IS NULL OR base::text = '' THEN
                    RETURN NULL;
                  END IF;
                  rewritten := regexp_replace(base::text, '''([^'']+)''', '''\1'':*', 'g');
                  RETURN to_tsquery(cfg, rewritten);
                EXCEPTION
                  WHEN OTHERS THEN
                    RETURN base;
                END;
                $fn$;

                CREATE OR REPLACE FUNCTION messaging.versatile_tsquery(config text, raw text)
                RETURNS tsquery
                LANGUAGE plpgsql
                STABLE
                AS $fn$
                DECLARE
                  cfg regconfig := config::regconfig;
                  src text := btrim(coalesce(raw, ''));
                  i int := 1;
                  n int;
                  ch text;
                  buf text := '';
                  in_quote boolean := false;
                  kinds text[] := ARRAY[]::text[];
                  vals text[] := ARRAY[]::text[];
                  token text;
                  piece tsquery;
                  groups tsquery[] := ARRAY[]::tsquery[];
                  current_and tsquery := NULL;
                  pending text := 'and';
                  k int;
                BEGIN
                  IF src = '' THEN
                    RETURN NULL;
                  END IF;

                  n := char_length(src);
                  WHILE i <= n LOOP
                    ch := substr(src, i, 1);
                    IF in_quote THEN
                      IF ch = '"' THEN
                        kinds := array_append(kinds, 'phrase');
                        vals := array_append(vals, btrim(buf));
                        buf := '';
                        in_quote := false;
                      ELSE
                        buf := buf || ch;
                      END IF;
                    ELSIF ch = '"' THEN
                      IF btrim(buf) <> '' THEN
                        kinds := array_append(kinds, 'word');
                        vals := array_append(vals, btrim(buf));
                        buf := '';
                      END IF;
                      in_quote := true;
                    ELSIF ch ~ '\s' THEN
                      IF btrim(buf) <> '' THEN
                        kinds := array_append(kinds, 'word');
                        vals := array_append(vals, btrim(buf));
                        buf := '';
                      END IF;
                    ELSE
                      buf := buf || ch;
                    END IF;
                    i := i + 1;
                  END LOOP;

                  IF in_quote THEN
                    IF btrim(buf) <> '' THEN
                      kinds := array_append(kinds, 'phrase');
                      vals := array_append(vals, btrim(buf));
                    END IF;
                  ELSIF btrim(buf) <> '' THEN
                    kinds := array_append(kinds, 'word');
                    vals := array_append(vals, btrim(buf));
                  END IF;

                  IF cardinality(kinds) = 0 THEN
                    RETURN NULL;
                  END IF;

                  FOR k IN 1..cardinality(kinds) LOOP
                    token := vals[k];
                    IF kinds[k] = 'word' AND upper(token) = 'OR' THEN
                      pending := 'or';
                      CONTINUE;
                    END IF;

                    IF kinds[k] = 'phrase' THEN
                      IF token = '' THEN
                        CONTINUE;
                      END IF;
                      piece := websearch_to_tsquery(cfg, '"' || token || '"');
                    ELSIF left(token, 1) = '-' AND char_length(token) > 1 THEN
                      piece := websearch_to_tsquery(cfg, substr(token, 2));
                      IF piece IS NULL OR piece::text = '' THEN
                        CONTINUE;
                      END IF;
                      piece := !! piece;
                    ELSE
                      piece := messaging.prefix_tsquery(cfg, token);
                    END IF;

                    IF piece IS NULL OR piece::text = '' THEN
                      CONTINUE;
                    END IF;

                    IF pending = 'or' THEN
                      IF current_and IS NOT NULL THEN
                        groups := array_append(groups, current_and);
                      END IF;
                      current_and := piece;
                      pending := 'and';
                    ELSE
                      current_and := CASE WHEN current_and IS NULL THEN piece ELSE current_and && piece END;
                    END IF;
                  END LOOP;

                  IF current_and IS NOT NULL THEN
                    groups := array_append(groups, current_and);
                  END IF;

                  IF cardinality(groups) = 0 THEN
                    RETURN websearch_to_tsquery(cfg, src);
                  END IF;

                  piece := groups[1];
                  IF cardinality(groups) > 1 THEN
                    FOR k IN 2..cardinality(groups) LOOP
                      piece := piece || groups[k];
                    END LOOP;
                  END IF;
                  RETURN piece;
                EXCEPTION
                  WHEN OTHERS THEN
                    BEGIN
                      RETURN websearch_to_tsquery(config::regconfig, coalesce(raw, ''));
                    EXCEPTION
                      WHEN OTHERS THEN
                        RETURN NULL;
                    END;
                END;
                $fn$;

                CREATE OR REPLACE FUNCTION messaging.plain_search_text(raw text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                AS $fn$
                  SELECT regexp_replace(coalesce(raw, ''), '[^[:alnum:]]+', ' ', 'g');
                $fn$;

                CREATE OR REPLACE FUNCTION messaging.phrase_boost(body text, raw text)
                RETURNS real
                LANGUAGE plpgsql
                STABLE
                AS $fn$
                DECLARE
                  src text := coalesce(raw, '');
                  i int := 1;
                  n int := char_length(src);
                  ch text;
                  buf text := '';
                  in_quote boolean := false;
                  phrase text;
                  needle text;
                  boost real := 0;
                  plain text := unaccent(lower(coalesce(body, '')));
                BEGIN
                  WHILE i <= n LOOP
                    ch := substr(src, i, 1);
                    IF in_quote THEN
                      IF ch = '"' THEN
                        phrase := btrim(buf);
                        buf := '';
                        in_quote := false;
                        IF phrase <> '' THEN
                          needle := replace(replace(replace(unaccent(lower(phrase)), E'\\', E'\\\\'), '%', E'\\%'), '_', E'\\_');
                          IF plain LIKE '%' || needle || '%' ESCAPE '\' THEN
                            boost := boost + 1;
                          END IF;
                        END IF;
                      ELSE
                        buf := buf || ch;
                      END IF;
                    ELSIF ch = '"' THEN
                      buf := '';
                      in_quote := true;
                    END IF;
                    i := i + 1;
                  END LOOP;
                  RETURN LEAST(boost, 2);
                END;
                $fn$;

                CREATE OR REPLACE FUNCTION messaging.messages_search_vector_update()
                RETURNS trigger AS $fn$
                BEGIN
                    IF NEW."DeletedAt" IS NOT NULL THEN
                        NEW.search_vector := NULL;
                    ELSE
                        NEW.search_vector := to_tsvector('public.portuguese_unaccent'::regconfig, coalesce(NEW."Body", ''));
                    END IF;
                    RETURN NEW;
                END;
                $fn$ LANGUAGE plpgsql;

                UPDATE messaging.messages
                SET search_vector = CASE
                    WHEN "DeletedAt" IS NOT NULL THEN NULL
                    ELSE to_tsvector('public.portuguese_unaccent'::regconfig, coalesce("Body", ''))
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION messaging.messages_search_vector_update()
                RETURNS trigger AS $fn$
                BEGIN
                    IF NEW."DeletedAt" IS NOT NULL THEN
                        NEW.search_vector := NULL;
                    ELSE
                        NEW.search_vector := to_tsvector('portuguese', coalesce(NEW."Body", ''));
                    END IF;
                    RETURN NEW;
                END;
                $fn$ LANGUAGE plpgsql;

                UPDATE messaging.messages
                SET search_vector = CASE
                    WHEN "DeletedAt" IS NOT NULL THEN NULL
                    ELSE to_tsvector('portuguese', coalesce("Body", ''))
                END;

                DROP FUNCTION IF EXISTS messaging.phrase_boost(text, text);
                DROP FUNCTION IF EXISTS messaging.plain_search_text(text);
                DROP FUNCTION IF EXISTS messaging.versatile_tsquery(text, text);
                DROP FUNCTION IF EXISTS messaging.prefix_tsquery(regconfig, text);
                DROP TEXT SEARCH CONFIGURATION IF EXISTS public.portuguese_unaccent;
                """);
        }
    }
}
