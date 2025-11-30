"""
Plot Performance Experiment Results

Reads results/quorum_experiment.csv and generates a chart showing
write_quorum vs average latency.

Usage:
  python tests/plot_results.py

Output:
  - results/quorum_latency_chart.png
"""

import csv
import os
from pathlib import Path

try:
    import matplotlib
    matplotlib.use('Agg')  # Use non-GUI backend for saving files
    import matplotlib.pyplot as plt
    HAS_MATPLOTLIB = True
except ImportError:
    HAS_MATPLOTLIB = False
    print("matplotlib not installed. Install with: pip install matplotlib")


def load_results(csv_file: str) -> dict:
    """Load results from CSV file"""
    data = {
        'quorum': [],
        'avg_latency': [],
        'median_latency': [],
        'min_latency': [],
        'max_latency': [],
        'stdev': []
    }
    
    with open(csv_file, 'r') as f:
        reader = csv.DictReader(f)
        for row in reader:
            data['quorum'].append(int(row['write_quorum']))
            data['avg_latency'].append(float(row['avg_latency_ms']))
            data['median_latency'].append(float(row['median_latency_ms']))
            data['min_latency'].append(float(row['min_latency_ms']))
            data['max_latency'].append(float(row['max_latency_ms']))
            data['stdev'].append(float(row['stdev_ms']))
    
    return data


def plot_latency_chart(data: dict, output_file: str):
    """Generate latency vs quorum chart"""
    if not HAS_MATPLOTLIB:
        print("Cannot generate chart without matplotlib")
        return
    
    fig, ax = plt.subplots(figsize=(10, 6))
    
    quorum = data['quorum']
    avg_lat = data['avg_latency']
    stdev = data['stdev']
    
    # Plot with error bars
    ax.errorbar(
        quorum, avg_lat, yerr=stdev,
        marker='o', markersize=10, linewidth=2, capsize=5,
        color='#2563eb', ecolor='#93c5fd',
        label='Average Latency (± std dev)'
    )
    
    # Also show median as secondary line
    ax.plot(
        quorum, data['median_latency'],
        marker='s', markersize=8, linewidth=2, linestyle='--',
        color='#16a34a', alpha=0.7,
        label='Median Latency'
    )
    
    # Styling
    ax.set_xlabel('Write Quorum', fontsize=12, fontweight='bold')
    ax.set_ylabel('Latency (ms)', fontsize=12, fontweight='bold')
    ax.set_title('Write Latency vs. Write Quorum\n(Single-Leader Replication)', fontsize=14, fontweight='bold')
    
    ax.set_xticks(quorum)
    ax.grid(True, alpha=0.3)
    ax.legend(loc='upper left')
    
    # Add annotations
    for i, (q, lat) in enumerate(zip(quorum, avg_lat)):
        ax.annotate(
            f'{lat:.1f}ms',
            (q, lat),
            textcoords="offset points",
            xytext=(0, 15),
            ha='center',
            fontsize=9,
            color='#1e40af'
        )
    
    plt.tight_layout()
    
    # Save
    os.makedirs(os.path.dirname(output_file) or '.', exist_ok=True)
    plt.savefig(output_file, dpi=150, bbox_inches='tight')
    print(f"Chart saved to: {output_file}")


def print_text_chart(data: dict):
    """Print a simple ASCII chart for environments without matplotlib"""
    print("\n" + "=" * 60)
    print("LATENCY vs QUORUM (text chart)")
    print("=" * 60)
    
    max_lat = max(data['avg_latency'])
    bar_width = 40
    
    for q, lat in zip(data['quorum'], data['avg_latency']):
        bar_len = int((lat / max_lat) * bar_width) if max_lat > 0 else 0
        bar = "█" * bar_len
        print(f"Quorum {q}: {bar} {lat:.2f} ms")
    
    print()


def main():
    csv_file = "results/quorum_experiment.csv"
    output_file = "results/quorum_latency_chart.png"
    
    if not os.path.exists(csv_file):
        print(f"Results file not found: {csv_file}")
        print("Run the experiment first: python tests/run_quorum_experiment.py")
        
        # Demo with sample data
        print("\nGenerating demo chart with sample data...")
        demo_data = {
            'quorum': [1, 2, 3, 4, 5],
            'avg_latency': [45.0, 85.0, 150.0, 280.0, 420.0],
            'median_latency': [40.0, 80.0, 140.0, 260.0, 400.0],
            'min_latency': [20.0, 40.0, 60.0, 100.0, 150.0],
            'max_latency': [100.0, 180.0, 320.0, 500.0, 800.0],
            'stdev': [15.0, 30.0, 60.0, 100.0, 150.0]
        }
        print_text_chart(demo_data)
        if HAS_MATPLOTLIB:
            plot_latency_chart(demo_data, "results/demo_chart.png")
        return
    
    print(f"Loading results from: {csv_file}")
    data = load_results(csv_file)
    
    print(f"Found {len(data['quorum'])} data points")
    
    # Always print text chart
    print_text_chart(data)
    
    # Generate image if matplotlib available
    if HAS_MATPLOTLIB:
        plot_latency_chart(data, output_file)
    else:
        print("\nTo generate PNG chart, install matplotlib:")
        print("  pip install matplotlib")


if __name__ == "__main__":
    main()
