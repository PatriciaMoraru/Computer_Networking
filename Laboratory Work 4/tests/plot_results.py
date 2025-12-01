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
        'p95_latency': [],
        'p99_latency': [],
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
            # Handle both old and new CSV formats
            data['p95_latency'].append(float(row.get('p95_latency_ms', row.get('max_latency_ms', 0))))
            data['p99_latency'].append(float(row.get('p99_latency_ms', row.get('max_latency_ms', 0))))
            data['stdev'].append(float(row['stdev_ms']))
    
    return data


def plot_latency_chart(data: dict, output_file: str):
    """Generate latency vs quorum chart with mean, median, p95, p99 - styled"""
    if not HAS_MATPLOTLIB:
        print("Cannot generate chart without matplotlib")
        return
    
    # Try different style names for compatibility with different matplotlib versions
    style_options = ['seaborn-v0_8-darkgrid', 'seaborn-darkgrid', 'ggplot', 'default']
    for style in style_options:
        try:
            plt.style.use(style)
            break
        except OSError:
            continue
    
    fig, ax = plt.subplots(figsize=(12, 7))
    
    # Set dark background for modern look
    fig.patch.set_facecolor('#1a1a2e')
    ax.set_facecolor('#16213e')
    
    quorum = data['quorum']
    
    # Convert to seconds for cleaner display
    mean_s = [x / 1000 for x in data['avg_latency']]
    median_s = [x / 1000 for x in data['median_latency']]
    p95_s = [x / 1000 for x in data['p95_latency']]
    p99_s = [x / 1000 for x in data['p99_latency']]
    
    # Vibrant color palette
    colors = {
        'mean': '#00d4ff',      # Cyan
        'median': '#ff6b6b',    # Coral
        'p95': '#4ecdc4',       # Teal
        'p99': '#ffe66d',       # Yellow
    }
    
    # Plot with glow effect (plot twice - thick transparent + thin solid)
    for metric, values, color, marker, label in [
        ('mean', mean_s, colors['mean'], 'o', 'Mean'),
        ('median', median_s, colors['median'], 's', 'Median'),
        ('p95', p95_s, colors['p95'], '^', 'P95'),
        ('p99', p99_s, colors['p99'], 'D', 'P99'),
    ]:
        # Glow effect
        ax.plot(quorum, values, marker=marker, markersize=12, linewidth=6,
                color=color, alpha=0.3)
        # Main line
        ax.plot(quorum, values, marker=marker, markersize=8, linewidth=2.5,
                color=color, label=label, markeredgecolor='white', markeredgewidth=1)
    
    # Styling
    title_color = '#ffffff'
    label_color = '#e0e0e0'
    
    ax.set_xlabel('Write Quorum', fontsize=14, fontweight='bold', color=label_color)
    ax.set_ylabel('Latency (seconds)', fontsize=14, fontweight='bold', color=label_color)
    ax.set_title('Quorum vs. Latency Performance\nSingle-Leader Replication with Random Delay [0, 1000ms]', 
                 fontsize=16, fontweight='bold', color=title_color, pad=20)
    
    # X-axis labels
    ax.set_xticks(quorum)
    ax.set_xticklabels([f'Q={q}' for q in quorum], fontsize=12, color=label_color)
    ax.tick_params(axis='y', colors=label_color, labelsize=11)
    
    # Grid styling
    ax.grid(True, alpha=0.2, color='#ffffff', linestyle='--')
    ax.set_axisbelow(True)
    
    # Legend with custom styling
    legend = ax.legend(loc='upper left', fontsize=11, framealpha=0.9,
                       facecolor='#2d3a4f', edgecolor='#4a5568')
    for text in legend.get_texts():
        text.set_color('#ffffff')
    
    # Add subtle border
    for spine in ax.spines.values():
        spine.set_color('#4a5568')
        spine.set_linewidth(1.5)
    
    # Add annotation for key insight
    max_quorum_idx = len(quorum) - 1
    ax.annotate(
        f'  {p99_s[max_quorum_idx]:.2f}s',
        xy=(quorum[max_quorum_idx], p99_s[max_quorum_idx]),
        xytext=(quorum[max_quorum_idx] + 0.15, p99_s[max_quorum_idx]),
        fontsize=10, color=colors['p99'], fontweight='bold',
        ha='left', va='center'
    )
    ax.annotate(
        f'  {mean_s[0]:.2f}s',
        xy=(quorum[0], mean_s[0]),
        xytext=(quorum[0] - 0.15, mean_s[0]),
        fontsize=10, color=colors['mean'], fontweight='bold',
        ha='right', va='center'
    )
    
    plt.tight_layout()
    
    # Save
    os.makedirs(os.path.dirname(output_file) or '.', exist_ok=True)
    plt.savefig(output_file, dpi=200, bbox_inches='tight', 
                facecolor=fig.get_facecolor(), edgecolor='none')
    print(f"Chart saved to: {output_file}")
    
    # Reset style for future plots
    plt.style.use('default')


def print_text_chart(data: dict):
    """Print a simple ASCII chart for environments without matplotlib"""
    print("\n" + "=" * 70)
    print("LATENCY vs QUORUM")
    print("=" * 70)
    print(f"{'Quorum':<8} {'Mean':<12} {'Median':<12} {'P95':<12} {'P99':<12}")
    print("-" * 70)
    
    for i, q in enumerate(data['quorum']):
        mean = data['avg_latency'][i]
        median = data['median_latency'][i]
        p95 = data['p95_latency'][i] if data['p95_latency'] else 0
        p99 = data['p99_latency'][i] if data['p99_latency'] else 0
        print(f"Q={q:<5} {mean:<12.1f} {median:<12.1f} {p95:<12.1f} {p99:<12.1f}")
    
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
            'avg_latency': [150.0, 300.0, 500.0, 600.0, 800.0],
            'median_latency': [140.0, 280.0, 480.0, 580.0, 780.0],
            'min_latency': [20.0, 40.0, 60.0, 100.0, 150.0],
            'max_latency': [400.0, 600.0, 900.0, 950.0, 1000.0],
            'p95_latency': [350.0, 550.0, 850.0, 900.0, 980.0],
            'p99_latency': [380.0, 580.0, 880.0, 940.0, 995.0],
            'stdev': [80.0, 120.0, 150.0, 140.0, 100.0]
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
