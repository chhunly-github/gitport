package gitport;

import java.awt.*;
import java.awt.geom.*;
import java.awt.image.BufferedImage;
import java.io.File;
import java.nio.file.Path;
import java.util.*;
import java.util.List;
import javax.imageio.ImageIO;

public class TestCommitGraph {
    public record RawCommit(String hash, String[] parents, String subject) {}

    public record Edge(int fromLane, int toLane) {}

    public static final class RowGraph {
        public int commitLane;
        public final Set<Integer> passThrough = new TreeSet<>();
        public final List<Edge> branchEdges = new ArrayList<>();
        public final List<Edge> mergeEdges = new ArrayList<>();
        public boolean isHead;
        public boolean isMerge;
    }

    public static final Color[] LANE_COLORS = {
        new Color(217, 87, 150), // Pink / Magenta (WebStorm master/HEAD lane)
        new Color(229, 192, 123), // Amber / Gold
        new Color(97, 175, 239),  // Cyan / Blue
        new Color(152, 195, 121), // Green
        new Color(198, 120, 221), // Purple
        new Color(224, 108, 117)  // Coral Red
    };

    public static List<RowGraph> buildGraph(List<RawCommit> commits) {
        List<RowGraph> result = new ArrayList<>();
        List<String> activeLanes = new ArrayList<>();

        for (int i = 0; i < commits.size(); i++) {
            RawCommit c = commits.get(i);
            RowGraph row = new RowGraph();
            row.isHead = (i == 0);
            row.isMerge = (c.parents.length > 1);

            int lane = activeLanes.indexOf(c.hash);
            if (lane == -1) {
                int slot = activeLanes.indexOf(null);
                if (slot == -1) {
                    lane = activeLanes.size();
                    activeLanes.add(c.hash);
                } else {
                    lane = slot;
                    activeLanes.set(slot, c.hash);
                }
            }
            row.commitLane = lane;

            // Pass through lanes for other active branches
            for (int l = 0; l < activeLanes.size(); l++) {
                if (activeLanes.get(l) != null && l != lane) {
                    row.passThrough.add(l);
                }
            }

            // Route parents
            if (c.parents.length == 0) {
                activeLanes.set(lane, null);
            } else {
                // Primary parent continues this lane
                activeLanes.set(lane, c.parents[0]);

                // Secondary parents (merge commits)
                for (int p = 1; p < c.parents.length; p++) {
                    String parentHash = c.parents[p];
                    int parentLane = activeLanes.indexOf(parentHash);
                    if (parentLane == -1) {
                        int slot = activeLanes.indexOf(null);
                        if (slot == -1) {
                            parentLane = activeLanes.size();
                            activeLanes.add(parentHash);
                        } else {
                            parentLane = slot;
                            activeLanes.set(slot, parentHash);
                        }
                    }
                    row.mergeEdges.add(new Edge(lane, parentLane));
                }
            }

            // Trim trailing nulls
            while (!activeLanes.isEmpty() && activeLanes.get(activeLanes.size() - 1) == null) {
                activeLanes.remove(activeLanes.size() - 1);
            }

            result.add(row);
        }
        return result;
    }

    public static void main(String[] args) throws Exception {
        Path repo = Path.of(".").toAbsolutePath();
        Portable p = new Portable(Path.of("dist/GitPort"));
        Git git = new Git(p, p.git(), repo);

        Git.Result r = git.run("log", "-n", "35", "--format=%H%x1f%P%x1f%s");
        List<RawCommit> commits = new ArrayList<>();
        for (String line : r.out().split("\n")) {
            if (line.isBlank()) continue;
            String[] parts = line.split("\u001f", -1);
            if (parts.length >= 3) {
                String hash = parts[0].strip();
                String[] parents = parts[1].strip().isEmpty() ? new String[0] : parts[1].strip().split("\\s+");
                String subj = parts[2].strip();
                commits.add(new RawCommit(hash, parents, subj));
            }
        }

        List<RowGraph> graph = buildGraph(commits);
        System.out.println("Built graph for " + graph.size() + " commits");

        int width = 800, rowHeight = 26;
        int height = commits.size() * rowHeight;
        BufferedImage img = new BufferedImage(width, height, BufferedImage.TYPE_INT_RGB);
        Graphics2D g = img.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_LCD_HRGB);
        g.setColor(new Color(0x152438));
        g.fillRect(0, 0, width, height);

        int laneSpacing = 16;
        int xOffset = 14;

        for (int i = 0; i < graph.size(); i++) {
            RowGraph row = graph.get(i);
            int y = i * rowHeight;
            int yCenter = y + rowHeight / 2;

            // Draw pass-through lines
            g.setStroke(new BasicStroke(1.8f));
            for (int l : row.passThrough) {
                int lx = xOffset + l * laneSpacing;
                g.setColor(LANE_COLORS[l % LANE_COLORS.length]);
                g.drawLine(lx, y, lx, y + rowHeight);
            }

            // Draw commit lane vertical lines
            int cx = xOffset + row.commitLane * laneSpacing;
            g.setColor(LANE_COLORS[row.commitLane % LANE_COLORS.length]);
            // Incoming from child
            if (i > 0) {
                g.drawLine(cx, y, cx, yCenter);
            }
            // Outgoing to parent
            if (i < graph.size() - 1) {
                g.drawLine(cx, yCenter, cx, y + rowHeight);
            }

            // Draw merge/branch curves
            for (Edge edge : row.mergeEdges) {
                int fx = xOffset + edge.fromLane * laneSpacing;
                int tx = xOffset + edge.toLane * laneSpacing;
                g.setColor(LANE_COLORS[edge.toLane % LANE_COLORS.length]);
                Path2D.Double curve = new Path2D.Double();
                curve.moveTo(fx, yCenter);
                curve.curveTo(fx, yCenter + 12, tx, yCenter + 4, tx, y + rowHeight);
                g.draw(curve);
            }

            // Draw commit circle
            int dotColorIdx = row.commitLane % LANE_COLORS.length;
            g.setColor(LANE_COLORS[dotColorIdx]);

            if (row.isHead) {
                // Concentric circle like WebStorm
                g.draw(new Ellipse2D.Double(cx - 5.5, yCenter - 5.5, 11, 11));
                g.fill(new Ellipse2D.Double(cx - 2.5, yCenter - 2.5, 5, 5));
            } else if (row.isMerge) {
                g.fill(new Ellipse2D.Double(cx - 4.5, yCenter - 4.5, 9, 9));
                g.setColor(Color.WHITE);
                g.fill(new Ellipse2D.Double(cx - 2, yCenter - 2, 4, 4));
            } else {
                g.fill(new Ellipse2D.Double(cx - 3.5, yCenter - 3.5, 7, 7));
            }

            // Draw subject
            g.setColor(new Color(0xE6EEF8));
            g.setFont(new Font("Segoe UI", Font.PLAIN, 12));
            int textX = xOffset + 4 * laneSpacing + 10;
            g.drawString(commits.get(i).subject, textX, yCenter + 4);
        }

        g.dispose();
        ImageIO.write(img, "PNG", new File("build/test_graph.png"));
        System.out.println("Written test_graph.png");
    }
}
