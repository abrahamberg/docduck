#!/bin/bash

# Integration Test Runner for DocDuck Providers
# This script helps run integration tests with proper environment setup

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to check if environment variables are set
check_env_vars() {
    local provider=$1
    shift
    local vars=("$@")
    local missing=()
    
    for var in "${vars[@]}"; do
        if [[ -z "${!var}" ]]; then
            missing+=("$var")
        fi
    done
    
    if [[ ${#missing[@]} -gt 0 ]]; then
        print_warning "$provider provider: Missing environment variables: ${missing[*]}"
        return 1
    else
        print_success "$provider provider: All required environment variables are set"
        return 0
    fi
}

# Function to run tests for a specific provider
run_provider_tests() {
    local provider=$1
    local test_class=$2
    
    print_status "Running $provider Provider Integration Tests..."
    
    if dotnet test --filter "ClassName~$test_class" --logger "console;verbosity=normal" --no-build; then
        print_success "$provider tests completed successfully"
    else
        print_error "$provider tests failed"
        return 1
    fi
}

# Main script
main() {
    print_status "DocDuck Provider Integration Test Runner"
    echo
    
    # Build the test project first
    print_status "Building test project..."
    if ! dotnet build; then
        print_error "Build failed"
        exit 1
    fi
    
    local test_local=true
    local test_s3=false
    local test_onedrive=false
    local failed_tests=()
    
    # Check which providers can be tested
    print_status "Checking provider configurations..."
    
    # Local provider (always available)
    print_success "Local provider: Ready (no configuration required)"
    
    # S3 provider
    if check_env_vars "S3" AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_TEST_BUCKET; then
        test_s3=true
    fi
    
    # OneDrive provider
    if check_env_vars "OneDrive" ONEDRIVE_TENANT_ID ONEDRIVE_CLIENT_ID ONEDRIVE_CLIENT_SECRET; then
        test_onedrive=true
    fi
    
    echo
    
    # Run tests based on command line arguments
    case "${1:-all}" in
        "local")
            print_status "Running only Local provider tests"
            run_provider_tests "Local" "LocalProviderIntegrationTests" || failed_tests+=("Local")
            ;;
        "s3")
            if [[ "$test_s3" == true ]]; then
                print_status "Running only S3 provider tests"
                run_provider_tests "S3" "S3ProviderIntegrationTests" || failed_tests+=("S3")
            else
                print_error "S3 provider not configured. Set required environment variables."
                exit 1
            fi
            ;;
        "onedrive")
            if [[ "$test_onedrive" == true ]]; then
                print_status "Running only OneDrive provider tests"
                run_provider_tests "OneDrive" "OneDriveProviderIntegrationTests" || failed_tests+=("OneDrive")
            else
                print_error "OneDrive provider not configured. Set required environment variables."
                exit 1
            fi
            ;;
        "all"|"")
            print_status "Running all available provider tests"
            
            # Always run local tests
            run_provider_tests "Local" "LocalProviderIntegrationTests" || failed_tests+=("Local")
            
            # Run S3 tests if configured
            if [[ "$test_s3" == true ]]; then
                run_provider_tests "S3" "S3ProviderIntegrationTests" || failed_tests+=("S3")
            else
                print_warning "Skipping S3 tests (not configured)"
            fi
            
            # Run OneDrive tests if configured
            if [[ "$test_onedrive" == true ]]; then
                run_provider_tests "OneDrive" "OneDriveProviderIntegrationTests" || failed_tests+=("OneDrive")
            else
                print_warning "Skipping OneDrive tests (not configured)"
            fi
            ;;
        "help"|"-h"|"--help")
            echo "Usage: $0 [provider]"
            echo ""
            echo "Providers:"
            echo "  local     - Run Local provider tests only"
            echo "  s3        - Run S3 provider tests only (requires AWS config)"
            echo "  onedrive  - Run OneDrive provider tests only (requires Azure config)"
            echo "  all       - Run all configured provider tests (default)"
            echo "  help      - Show this help message"
            echo ""
            echo "Required Environment Variables:"
            echo ""
            echo "S3 Provider:"
            echo "  AWS_ACCESS_KEY_ID"
            echo "  AWS_SECRET_ACCESS_KEY"
            echo "  AWS_TEST_BUCKET"
            echo "  AWS_REGION (optional, defaults to us-east-1)"
            echo ""
            echo "OneDrive Provider:"
            echo "  ONEDRIVE_TENANT_ID"
            echo "  ONEDRIVE_CLIENT_ID"
            echo "  ONEDRIVE_CLIENT_SECRET"
            echo "  ONEDRIVE_DRIVE_ID (optional)"
            echo "  ONEDRIVE_SITE_ID (optional)"
            echo "  ONEDRIVE_FOLDER_PATH (optional, defaults to /Shared Documents/Docs)"
            exit 0
            ;;
        *)
            print_error "Unknown provider: $1"
            print_status "Use '$0 help' for usage information"
            exit 1
            ;;
    esac
    
    echo
    
    # Summary
    if [[ ${#failed_tests[@]} -eq 0 ]]; then
        print_success "All integration tests completed successfully!"
        exit 0
    else
        print_error "Some tests failed: ${failed_tests[*]}"
        exit 1
    fi
}

# Run main function
main "$@"