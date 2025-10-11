#!/bin/bash
# Database management utility for DocDuck
# Usage: ./db-util.sh [command]

set -e

# Load environment variables if .env exists
if [ -f .env ]; then
    export $(cat .env | grep -v '^#' | xargs)
fi

# Default connection string from environment or use default
DB_CONN="${DB_CONNECTION_STRING:-Host=localhost;Database=vectors;Username=postgres;Password=password}"

# Parse connection string components
DB_HOST=$(echo "$DB_CONN" | grep -oP 'Host=\K[^;]+' || echo "localhost")
DB_PORT=$(echo "$DB_CONN" | grep -oP 'Port=\K[^;]+' || echo "5432")
DB_NAME=$(echo "$DB_CONN" | grep -oP 'Database=\K[^;]+' || echo "vectors")
DB_USER=$(echo "$DB_CONN" | grep -oP 'Username=\K[^;]+' || echo "postgres")
DB_PASS=$(echo "$DB_CONN" | grep -oP 'Password=\K[^;]+' || echo "password")

export PGPASSWORD="$DB_PASS"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

print_success() {
    echo -e "${GREEN}✓${NC} $1"
}

print_error() {
    echo -e "${RED}✗${NC} $1"
}

print_info() {
    echo -e "${BLUE}ℹ${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}⚠${NC} $1"
}

# Check if psql is installed
check_psql() {
    if ! command -v psql &> /dev/null; then
        print_error "psql is not installed. Please install PostgreSQL client tools."
        exit 1
    fi
}

# Test database connection
test_connection() {
    print_info "Testing connection to PostgreSQL..."
    if psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "SELECT version();" > /dev/null 2>&1; then
        print_success "Connected to PostgreSQL successfully"
        psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "SELECT version();" | head -n 3
        return 0
    else
        print_error "Failed to connect to PostgreSQL"
        print_info "Connection details: $DB_HOST:$DB_PORT/$DB_NAME as $DB_USER"
        return 1
    fi
}

# Initialize database schema
init_schema() {
    print_info "Initializing database schema..."
    if [ ! -f "sql/01-init-schema.sql" ]; then
        print_error "Schema file not found: sql/01-init-schema.sql"
        exit 1
    fi
    
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f sql/01-init-schema.sql
    print_success "Schema initialized successfully"
}

# Check if pgvector extension is installed
check_pgvector() {
    print_info "Checking pgvector extension..."
    result=$(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM pg_available_extensions WHERE name = 'vector' AND installed_version IS NOT NULL;")
    
    if [ "$result" -eq 1 ]; then
        print_success "pgvector extension is installed"
        version=$(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT installed_version FROM pg_available_extensions WHERE name = 'vector';")
        print_info "Version: $version"
        return 0
    else
        print_error "pgvector extension is not installed"
        print_info "Install with: CREATE EXTENSION vector; (requires superuser)"
        return 1
    fi
}

# Show database statistics
show_stats() {
    print_info "Database statistics:"
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" << 'EOF'
SELECT 
    'Total Documents' AS metric,
    COUNT(DISTINCT doc_id)::TEXT AS value
FROM docs_chunks
UNION ALL
SELECT 
    'Total Chunks',
    COUNT(*)::TEXT
FROM docs_chunks
UNION ALL
SELECT 
    'Avg Chunks/Doc',
    ROUND(COUNT(*)::NUMERIC / NULLIF(COUNT(DISTINCT doc_id), 0), 1)::TEXT
FROM docs_chunks
UNION ALL
SELECT 
    'Database Size',
    pg_size_pretty(pg_database_size(current_database()))
UNION ALL
SELECT 
    'Table Size',
    pg_size_pretty(pg_total_relation_size('docs_chunks'));
EOF
}

# Show recent documents
show_recent() {
    print_info "Recently indexed documents (last 10):"
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" << 'EOF'
SELECT 
    filename,
    COUNT(*) AS chunk_count,
    MAX(created_at) AS last_indexed
FROM docs_chunks
GROUP BY filename
ORDER BY MAX(created_at) DESC
LIMIT 10;
EOF
}

# Run maintenance tasks
run_maintenance() {
    print_info "Running database maintenance..."
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" << 'EOF'
-- Update statistics
ANALYZE docs_chunks;
ANALYZE docs_files;

-- Vacuum
VACUUM ANALYZE docs_chunks;
VACUUM ANALYZE docs_files;

SELECT 'Maintenance complete' AS status;
EOF
    print_success "Maintenance tasks completed"
}

# Health check
health_check() {
    print_info "Running health check..."
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" << 'EOF'
DO $$
DECLARE
    chunk_count INT;
    dead_pct NUMERIC;
    index_scans BIGINT;
BEGIN
    SELECT COUNT(*) INTO chunk_count FROM docs_chunks;
    
    SELECT ROUND(100.0 * n_dead_tup / NULLIF(n_live_tup + n_dead_tup, 0), 2)
    INTO dead_pct
    FROM pg_stat_user_tables
    WHERE tablename = 'docs_chunks';
    
    SELECT idx_scan INTO index_scans
    FROM pg_stat_user_indexes
    WHERE indexrelname = 'docs_chunks_embedding_idx';
    
    RAISE NOTICE '=== Health Check ===';
    RAISE NOTICE 'Total chunks: %', chunk_count;
    RAISE NOTICE 'Dead row percentage: %', COALESCE(dead_pct, 0);
    RAISE NOTICE 'Vector index scans: %', COALESCE(index_scans, 0);
    
    IF dead_pct > 20 THEN
        RAISE NOTICE 'RECOMMENDATION: Run maintenance (dead rows > 20%%)';
    END IF;
    
    IF index_scans IS NULL OR index_scans = 0 THEN
        RAISE WARNING 'Vector index has never been used!';
    END IF;
    
    RAISE NOTICE '===================';
END $$;
EOF
}

# Backup database
backup_db() {
    backup_file="docduck_backup_$(date +%Y%m%d_%H%M%S).sql"
    print_info "Creating backup: $backup_file"
    
    pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" \
        -t docs_chunks -t docs_files \
        --no-owner --no-acl -f "$backup_file"
    
    print_success "Backup created: $backup_file"
    ls -lh "$backup_file"
}

# Reset database (destructive!)
reset_db() {
    print_warning "This will DELETE ALL DATA in docs_chunks and docs_files tables!"
    read -p "Are you sure? Type 'yes' to confirm: " confirmation
    
    if [ "$confirmation" != "yes" ]; then
        print_info "Reset cancelled"
        return
    fi
    
    print_info "Resetting database..."
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" << 'EOF'
TRUNCATE docs_chunks RESTART IDENTITY CASCADE;
TRUNCATE docs_files RESTART IDENTITY CASCADE;
VACUUM FULL docs_chunks;
VACUUM FULL docs_files;
SELECT 'Database reset complete' AS status;
EOF
    print_success "Database reset completed"
}

# Open psql shell
open_shell() {
    print_info "Opening PostgreSQL shell..."
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME"
}

# Show help
show_help() {
    cat << EOF
DocDuck Database Utility

Usage: $0 [command]

Commands:
    test         Test database connection
    init         Initialize database schema (creates tables and indexes)
    check        Check if pgvector extension is installed
    stats        Show database statistics
    recent       Show recently indexed documents
    health       Run health check
    maintain     Run maintenance tasks (VACUUM, ANALYZE)
    backup       Create database backup
    reset        Reset database (DESTRUCTIVE - deletes all data!)
    shell        Open PostgreSQL shell
    help         Show this help message

Environment:
    DB_CONNECTION_STRING    PostgreSQL connection string (from .env or environment)
    
Examples:
    $0 test               # Test connection
    $0 init               # Initialize schema
    $0 stats              # View statistics
    $0 health             # Run health check
    $0 maintain           # Run maintenance

Connection Details:
    Host: $DB_HOST
    Port: $DB_PORT
    Database: $DB_NAME
    Username: $DB_USER

EOF
}

# Main command dispatcher
case "${1:-help}" in
    test)
        check_psql
        test_connection
        ;;
    init)
        check_psql
        test_connection
        init_schema
        ;;
    check)
        check_psql
        test_connection
        check_pgvector
        ;;
    stats)
        check_psql
        show_stats
        ;;
    recent)
        check_psql
        show_recent
        ;;
    health)
        check_psql
        health_check
        ;;
    maintain)
        check_psql
        run_maintenance
        ;;
    backup)
        check_psql
        backup_db
        ;;
    reset)
        check_psql
        reset_db
        ;;
    shell)
        check_psql
        open_shell
        ;;
    help|--help|-h)
        show_help
        ;;
    *)
        print_error "Unknown command: $1"
        echo ""
        show_help
        exit 1
        ;;
esac
