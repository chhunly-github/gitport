package gitport;

import javax.swing.*;
import javax.swing.border.*;
import javax.swing.table.*;
import javax.swing.tree.*;
import java.awt.*;
import java.awt.datatransfer.StringSelection;
import java.awt.event.*;
import java.awt.geom.*;
import java.util.*;
import java.util.List;

/** WebStorm-style Git Log 2-pane view: Commit Graph DAG & Search (Center) + Hierarchical Changed Files Tree & Inspector (Right). */
public final class GitLogView extends JPanel {
    public record Commit(
        String hash,
        String[] parents,
        String shortHash,
        String author,
        String email,
        String date,
        String subject,
        String refs
    ) {
        public boolean isWorkingTree() {
            return "WORKING_TREE".equals(hash);
        }
    }

    public record CommitFile(String status, String path) {}

    public record Edge(int fromLane, int toLane) {}

    public static final class RowGraph {
        public int commitLane = 0;
        public final Set<Integer> passThrough = new TreeSet<>();
        public final List<Edge> mergeEdges = new ArrayList<>();
        public boolean isHead = false;
        public boolean isMerge = false;
        public int totalLanes = 1;
    }

    public static final Color[] LANE_COLORS = {
        new Color(217, 87, 150), // Pink / Magenta (WebStorm master/HEAD lane)
        new Color(229, 192, 123), // Amber / Gold
        new Color(97, 175, 239),  // Cyan / Blue
        new Color(152, 195, 121), // Green
        new Color(198, 120, 221), // Purple
        new Color(224, 108, 117)  // Coral Red
    };

    public interface Callbacks {
        void checkout(String ref);
        void newBranch(String baseRef);
        void newWorktree(String baseRef);
        void merge(String ref, boolean fetchFirst);
        void pull(String ref, boolean rebase);
        void rebase(String ref);
        void compare(String ref);
        void deleteBranch(String ref);
        void showDiff(String commitHash, String file);
        void pushUpTo(String commitHash);
        void newTag(String commitHash);
        void commitWorkingTree();
        void stashWorkingTree();
        void log(String msg);
        void error(String msg);
    }

    private final Callbacks callbacks;
    private Git git;
    private String currentBranch = "";
    private List<Git.Change> uncommittedChanges = Collections.emptyList();
    private final List<Commit> allCommits = new ArrayList<>();
    private final Map<Commit, RowGraph> commitGraphs = new HashMap<>();
    private Commit workingTreeCommit;

    private final DefaultTableModel commitModel = new DefaultTableModel(new String[]{"Description", "Author", "Date"}, 0) {
        public boolean isCellEditable(int r, int c) { return false; }
    };
    private final JTable commitTable = new JTable(commitModel);
    private final JTextField filterInput = new HintTextField("Text or hash...");

    // Right pane: changed files tree + inspector
    private final JLabel fileCountLabel = new JLabel("0 files");
    private boolean isTreeMode = true;
    private final CardLayout filesCards = new CardLayout();
    private final JPanel filesCardPanel = new JPanel(filesCards);

    private final DefaultMutableTreeNode treeRoot = new DefaultMutableTreeNode("Root");
    private final DefaultTreeModel treeModel = new DefaultTreeModel(treeRoot);
    private final JTree filesTree = new JTree(treeModel);

    private final DefaultTableModel filesTableModel = new DefaultTableModel(new String[]{"State", "File"}, 0) {
        public boolean isCellEditable(int r, int c) { return false; }
    };
    private final JTable filesTable = new JTable(filesTableModel);

    private final JTextArea commitBodyArea = new JTextArea();
    private final JLabel commitHashLabel = new JLabel();
    private final JLabel commitAuthorLabel = new JLabel();
    private final JPanel commitPillsPanel = new JPanel(new FlowLayout(FlowLayout.LEFT, 4, 2));
    private final JLabel commitBranchesSummary = new JLabel();
    private Commit selectedCommit;

    public GitLogView(Callbacks callbacks) {
        super(new BorderLayout());
        this.callbacks = callbacks;

        setBackground(AtomTheme.EDITOR);

        // Center Pane: Filter + Commit Table
        JPanel centerPane = new JPanel(new BorderLayout(0, 6));
        centerPane.setBackground(AtomTheme.EDITOR);
        centerPane.setBorder(new EmptyBorder(8, 10, 8, 8));

        JPanel filterBar = new JPanel(new BorderLayout(6, 0));
        filterBar.setOpaque(false);
        JLabel glass = new JLabel(Icons.get("search", AtomTheme.MUTED, 14));
        filterBar.add(glass, BorderLayout.WEST);
        filterInput.setFont(AtomTheme.UI.deriveFont(12f));
        filterInput.setBackground(AtomTheme.HEADER);
        filterInput.putClientProperty("JTextField.placeholderText", "Text or hash...");
        filterBar.add(filterInput, BorderLayout.CENTER);
        centerPane.add(filterBar, BorderLayout.NORTH);

        commitTable.setRowHeight(26);
        commitTable.setBackground(AtomTheme.EDITOR);
        commitTable.setForeground(AtomTheme.TEXT);
        commitTable.setSelectionBackground(AtomTheme.SELECT);
        commitTable.setSelectionForeground(AtomTheme.BRIGHT);
        commitTable.setShowGrid(false);
        commitTable.setSelectionMode(ListSelectionModel.SINGLE_SELECTION);
        commitTable.getColumnModel().getColumn(0).setPreferredWidth(450);
        commitTable.getColumnModel().getColumn(1).setPreferredWidth(130);
        commitTable.getColumnModel().getColumn(2).setPreferredWidth(140);
        commitTable.setDefaultRenderer(Object.class, new CommitCellRenderer());

        JScrollPane commitScroll = new JScrollPane(commitTable);
        commitScroll.setBorder(new EmptyBorder(0, 0, 0, 0));
        commitScroll.getViewport().setBackground(AtomTheme.EDITOR);
        centerPane.add(commitScroll, BorderLayout.CENTER);

        // Right Pane: Changed Files (Tree / Flat) + Commit Inspector
        JPanel rightPane = new JPanel(new BorderLayout(0, 8));
        rightPane.setBackground(AtomTheme.HEADER);
        rightPane.setPreferredSize(new Dimension(390, 500));
        rightPane.setMinimumSize(new Dimension(280, 300));
        rightPane.setBorder(new EmptyBorder(8, 10, 8, 10));

        // Files Header with view mode toggles
        JPanel filesHeader = new JPanel(new BorderLayout());
        filesHeader.setOpaque(false);
        fileCountLabel.setFont(AtomTheme.UI.deriveFont(Font.BOLD, 12f));
        fileCountLabel.setForeground(AtomTheme.SECONDARY);
        filesHeader.add(fileCountLabel, BorderLayout.WEST);

        JPanel filesTools = new JPanel(new FlowLayout(FlowLayout.RIGHT, 2, 0));
        filesTools.setOpaque(false);

        JButton toggleViewBtn = new JButton(Icons.get("repo", AtomTheme.SECONDARY, 14));
        AtomTheme.button(toggleViewBtn, false);
        toggleViewBtn.setToolTipText("Toggle Tree / Flat view");
        toggleViewBtn.setPreferredSize(new Dimension(26, 24));
        toggleViewBtn.addActionListener(e -> {
            isTreeMode = !isTreeMode;
            toggleViewBtn.setIcon(Icons.get(isTreeMode ? "repo" : "file", AtomTheme.BLUE, 14));
            filesCards.show(filesCardPanel, isTreeMode ? "tree" : "flat");
        });

        JButton expandAllBtn = new JButton(Icons.get("plus", AtomTheme.SECONDARY, 14));
        AtomTheme.button(expandAllBtn, false);
        expandAllBtn.setToolTipText("Expand All");
        expandAllBtn.setPreferredSize(new Dimension(26, 24));
        expandAllBtn.addActionListener(e -> expandAll(filesTree, true));

        JButton collapseAllBtn = new JButton(Icons.get("minus", AtomTheme.SECONDARY, 14));
        AtomTheme.button(collapseAllBtn, false);
        collapseAllBtn.setToolTipText("Collapse All");
        collapseAllBtn.setPreferredSize(new Dimension(26, 24));
        collapseAllBtn.addActionListener(e -> expandAll(filesTree, false));

        filesTools.add(toggleViewBtn);
        filesTools.add(expandAllBtn);
        filesTools.add(collapseAllBtn);
        filesHeader.add(filesTools, BorderLayout.EAST);
        rightPane.add(filesHeader, BorderLayout.NORTH);

        // Files Tree configuration
        filesTree.setRootVisible(true);
        filesTree.setShowsRootHandles(true);
        filesTree.setRowHeight(24);
        filesTree.setBackground(AtomTheme.HEADER);
        filesTree.setFont(AtomTheme.UI.deriveFont(12f));
        filesTree.getSelectionModel().setSelectionMode(TreeSelectionModel.SINGLE_TREE_SELECTION);
        filesTree.setCellRenderer(new FileTreeCellRenderer());

        JScrollPane treeScroll = new JScrollPane(filesTree);
        treeScroll.setBorder(new EmptyBorder(0, 0, 0, 0));
        treeScroll.getViewport().setBackground(AtomTheme.HEADER);

        // Files Flat Table configuration
        filesTable.setRowHeight(24);
        filesTable.setBackground(AtomTheme.HEADER);
        filesTable.setForeground(AtomTheme.TEXT);
        filesTable.setShowGrid(false);
        filesTable.setSelectionMode(ListSelectionModel.SINGLE_SELECTION);
        filesTable.getColumnModel().getColumn(0).setMaxWidth(48);
        filesTable.getColumnModel().getColumn(0).setPreferredWidth(40);
        filesTable.setDefaultRenderer(Object.class, new FileTableCellRenderer());

        JScrollPane tableScroll = new JScrollPane(filesTable);
        tableScroll.setBorder(new EmptyBorder(0, 0, 0, 0));
        tableScroll.getViewport().setBackground(AtomTheme.HEADER);

        filesCardPanel.add(treeScroll, "tree");
        filesCardPanel.add(tableScroll, "flat");

        // Commit Inspector Card at bottom of right pane
        JPanel inspectorCard = new JPanel(new BorderLayout(0, 6));
        inspectorCard.setBackground(AtomTheme.TREE);
        inspectorCard.setBorder(new CompoundBorder(new MatteBorder(1, 0, 0, 0, AtomTheme.BORDER), new EmptyBorder(10, 8, 8, 8)));

        commitBodyArea.setFont(AtomTheme.UI.deriveFont(Font.BOLD, 12f));
        commitBodyArea.setBackground(AtomTheme.TREE);
        commitBodyArea.setForeground(AtomTheme.BRIGHT);
        commitBodyArea.setEditable(false);
        commitBodyArea.setLineWrap(true);
        commitBodyArea.setWrapStyleWord(true);
        commitBodyArea.setRows(2);
        JScrollPane bodyScroll = new JScrollPane(commitBodyArea);
        bodyScroll.setBorder(new EmptyBorder(0, 0, 0, 0));
        bodyScroll.getViewport().setBackground(AtomTheme.TREE);

        JPanel metaPanel = new JPanel(new GridLayout(0, 1, 0, 3));
        metaPanel.setOpaque(false);
        commitHashLabel.setFont(AtomTheme.CODE.deriveFont(11.5f));
        commitHashLabel.setForeground(AtomTheme.BLUE);
        commitAuthorLabel.setFont(AtomTheme.UI.deriveFont(11.5f));
        commitAuthorLabel.setForeground(AtomTheme.SECONDARY);
        commitPillsPanel.setOpaque(false);
        commitBranchesSummary.setFont(AtomTheme.UI.deriveFont(11f));
        commitBranchesSummary.setForeground(AtomTheme.MUTED);

        metaPanel.add(commitHashLabel);
        metaPanel.add(commitAuthorLabel);
        metaPanel.add(commitPillsPanel);
        metaPanel.add(commitBranchesSummary);

        inspectorCard.add(bodyScroll, BorderLayout.NORTH);
        inspectorCard.add(metaPanel, BorderLayout.CENTER);

        JSplitPane rightSplit = new JSplitPane(JSplitPane.VERTICAL_SPLIT, filesCardPanel, inspectorCard);
        rightSplit.setResizeWeight(0.68);
        rightSplit.setDividerSize(3);
        rightSplit.setBorder(new EmptyBorder(0, 0, 0, 0));
        rightPane.add(rightSplit, BorderLayout.CENTER);

        // Main Splitter
        JSplitPane mainSplit = new JSplitPane(JSplitPane.HORIZONTAL_SPLIT, centerPane, rightPane);
        mainSplit.setResizeWeight(0.66);
        mainSplit.setDividerSize(3);
        mainSplit.setBorder(new EmptyBorder(0, 0, 0, 0));
        add(mainSplit, BorderLayout.CENTER);

        // Listeners
        commitTable.getSelectionModel().addListSelectionListener(e -> {
            if (!e.getValueIsAdjusting()) {
                int row = commitTable.getSelectedRow();
                if (row >= 0 && row < commitModel.getRowCount()) {
                    Object val = commitModel.getValueAt(row, 0);
                    if (val instanceof Commit c) {
                        selectCommit(c);
                    }
                }
            }
        });

        filesTree.addMouseListener(new MouseAdapter() {
            public void mouseClicked(MouseEvent e) {
                if (e.getClickCount() == 2 && SwingUtilities.isLeftMouseButton(e)) {
                    TreePath tp = filesTree.getPathForLocation(e.getX(), e.getY());
                    if (tp != null && tp.getLastPathComponent() instanceof DefaultMutableTreeNode n) {
                        if (n.getUserObject() instanceof FileEntry fe && selectedCommit != null) {
                            callbacks.showDiff(selectedCommit.hash(), fe.path);
                        }
                    }
                }
            }
        });

        filesTable.addMouseListener(new MouseAdapter() {
            public void mouseClicked(MouseEvent e) {
                if (e.getClickCount() == 2 && SwingUtilities.isLeftMouseButton(e)) {
                    int row = filesTable.getSelectedRow();
                    if (row >= 0 && selectedCommit != null) {
                        String file = (String) filesTableModel.getValueAt(row, 1);
                        callbacks.showDiff(selectedCommit.hash(), file);
                    }
                }
            }
        });

        commitTable.addMouseListener(new MouseAdapter() {
            public void mousePressed(MouseEvent e) { popup(e); }
            public void mouseReleased(MouseEvent e) { popup(e); }
        });

        filterInput.getDocument().addDocumentListener(new javax.swing.event.DocumentListener() {
            public void insertUpdate(javax.swing.event.DocumentEvent e) { filterRows(); }
            public void removeUpdate(javax.swing.event.DocumentEvent e) { filterRows(); }
            public void changedUpdate(javax.swing.event.DocumentEvent e) { filterRows(); }
        });
    }

    public void update(Git git, String currentBranch, List<Git.Change> uncommitted) {
        this.git = git;
        this.currentBranch = currentBranch != null ? currentBranch : "";
        this.uncommittedChanges = uncommitted != null ? uncommitted : Collections.emptyList();

        if (git == null || git.repo == null) {
            allCommits.clear();
            commitGraphs.clear();
            commitModel.setRowCount(0);
            clearInspector();
            return;
        }

        SwingWorker<List<Commit>, Void> worker = new SwingWorker<>() {
            protected List<Commit> doInBackground() throws Exception {
                List<Commit> list = new ArrayList<>();
                Git.Result r = git.run("log", "-n", "300", "--format=%H%x1f%P%x1f%h%x1f%an%x1f%ae%x1f%ad%x1f%s%x1f%d", "--date=format:%m/%d/%y, %I:%M %p");
                if (r.code() != 0) return list;

                String[] lines = r.out().split("\n");
                for (String line : lines) {
                    if (line.isBlank()) continue;
                    String[] parts = line.split("\u001f", -1);
                    if (parts.length >= 7) {
                        String hash = parts[0].strip();
                        String[] parents = parts[1].strip().isEmpty() ? new String[0] : parts[1].strip().split("\\s+");
                        String shortHash = parts[2].strip();
                        String author = parts[3].strip();
                        String email = parts[4].strip();
                        String date = parts[5].strip();
                        String subject = parts[6].strip();
                        String refs = parts.length > 7 ? parts[7].strip() : "";
                        list.add(new Commit(hash, parents, shortHash, author, email, date, subject, refs));
                    }
                }
                return list;
            }

            protected void done() {
                try {
                    allCommits.clear();
                    allCommits.addAll(get());
                    computeGraphLanes();

                    if (!uncommittedChanges.isEmpty()) {
                        workingTreeCommit = new Commit(
                            "WORKING_TREE",
                            new String[0],
                            "local",
                            "Working tree",
                            "",
                            "Uncommitted",
                            "Uncommitted changes (" + uncommittedChanges.size() + " files)",
                            "* Working tree"
                        );
                    } else {
                        workingTreeCommit = null;
                    }

                    filterRows();
                    if (commitTable.getRowCount() > 0) {
                        commitTable.setRowSelectionInterval(0, 0);
                    }
                } catch (Exception ignored) {}
            }
        };
        worker.execute();
    }

    private void computeGraphLanes() {
        commitGraphs.clear();
        List<String> activeLanes = new ArrayList<>();
        int maxLanesEncountered = 1;

        for (int i = 0; i < allCommits.size(); i++) {
            Commit c = allCommits.get(i);
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

            for (int l = 0; l < activeLanes.size(); l++) {
                if (activeLanes.get(l) != null && l != lane) {
                    row.passThrough.add(l);
                }
            }

            if (c.parents.length == 0) {
                activeLanes.set(lane, null);
            } else {
                activeLanes.set(lane, c.parents[0]);

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

            while (!activeLanes.isEmpty() && activeLanes.get(activeLanes.size() - 1) == null) {
                activeLanes.remove(activeLanes.size() - 1);
            }

            maxLanesEncountered = Math.max(maxLanesEncountered, activeLanes.size() + 1);
            row.totalLanes = maxLanesEncountered;
            commitGraphs.put(c, row);
        }
    }

    public void update(Git git) {
        update(git, currentBranch, uncommittedChanges);
    }

    private void filterRows() {
        String q = filterInput.getText().strip().toLowerCase(Locale.ROOT);
        commitModel.setRowCount(0);

        if (workingTreeCommit != null && (q.isEmpty() || workingTreeCommit.subject.toLowerCase(Locale.ROOT).contains(q))) {
            commitModel.addRow(new Object[]{workingTreeCommit, workingTreeCommit.author, workingTreeCommit.date});
        }

        for (Commit c : allCommits) {
            if (q.isEmpty() || c.subject.toLowerCase(Locale.ROOT).contains(q)
                || c.shortHash.toLowerCase(Locale.ROOT).contains(q)
                || c.author.toLowerCase(Locale.ROOT).contains(q)
                || c.refs.toLowerCase(Locale.ROOT).contains(q)) {
                commitModel.addRow(new Object[]{c, c.author, c.date});
            }
        }
    }

    private void clearInspector() {
        treeRoot.removeAllChildren();
        treeModel.reload();
        filesTableModel.setRowCount(0);
        fileCountLabel.setText("0 files");
        commitBodyArea.setText("");
        commitHashLabel.setText("");
        commitAuthorLabel.setText("");
        commitPillsPanel.removeAll();
        commitBranchesSummary.setText("");
    }

    private void selectCommit(Commit c) {
        this.selectedCommit = c;

        if (c.isWorkingTree()) {
            commitBodyArea.setText("Working Tree Changes\nLocal modifications in the workspace on branch '" + currentBranch + "'");
            commitHashLabel.setText("Uncommitted changes");
            commitAuthorLabel.setText("Workspace active files");
            commitPillsPanel.removeAll();
            commitPillsPanel.add(makeBranchPill("Working tree", AtomTheme.GOLD, AtomTheme.INK));
            commitBranchesSummary.setText("On branch: " + currentBranch);

            List<CommitFile> files = new ArrayList<>();
            for (Git.Change ch : uncommittedChanges) {
                files.add(new CommitFile(ch.xy().strip(), ch.path()));
            }
            populateFiles(files);
            return;
        }

        commitBodyArea.setText(c.subject);
        commitHashLabel.setText(c.shortHash + "  (" + c.hash + ")");
        commitAuthorLabel.setText(c.author + " <" + c.email + ">  on " + c.date);

        // Branch pills
        commitPillsPanel.removeAll();
        List<String> branches = parseBranches(c.refs);
        for (String br : branches) {
            Color bg = br.startsWith("origin/") || br.startsWith("upstream/") ? new Color(110, 60, 140) : AtomTheme.SELECT;
            commitPillsPanel.add(makeBranchPill(br, bg, AtomTheme.BRIGHT));
        }
        commitBranchesSummary.setText(branches.isEmpty() ? "" : "In " + branches.size() + " branches: " + String.join(", ", branches));
        commitPillsPanel.revalidate();
        commitPillsPanel.repaint();

        if (git == null) return;

        SwingWorker<List<CommitFile>, Void> worker = new SwingWorker<>() {
            protected List<CommitFile> doInBackground() throws Exception {
                List<CommitFile> files = new ArrayList<>();
                Git.Result r = git.run("show", "--name-status", "--oneline", "-M", "-C", c.hash);
                if (r.code() != 0) return files;

                String[] lines = r.out().split("\n");
                for (int i = 1; i < lines.length; i++) {
                    String line = lines[i].strip();
                    if (line.isEmpty()) continue;
                    String[] tokens = line.split("\\s+", 2);
                    if (tokens.length == 2) {
                        files.add(new CommitFile(tokens[0], tokens[1]));
                    }
                }
                return files;
            }

            protected void done() {
                try {
                    populateFiles(get());
                } catch (Exception ignored) {}
            }
        };
        worker.execute();
    }

    private void populateFiles(List<CommitFile> files) {
        fileCountLabel.setText(files.size() + (files.size() == 1 ? " file" : " files"));

        // 1. Populate Flat Table
        filesTableModel.setRowCount(0);
        for (CommitFile f : files) {
            filesTableModel.addRow(new Object[]{f.status, f.path});
        }

        // 2. Build Directory Tree
        treeRoot.removeAllChildren();
        treeRoot.setUserObject(new DirEntry("", "", files.size()));

        Map<String, DefaultMutableTreeNode> dirMap = new HashMap<>();

        for (CommitFile f : files) {
            String p = f.path.replace('\\', '/');
            int lastSlash = p.lastIndexOf('/');
            if (lastSlash >= 0) {
                String dirPath = p.substring(0, lastSlash);
                String fileName = p.substring(lastSlash + 1);

                DefaultMutableTreeNode dirNode = getOrCreateDirNode(treeRoot, dirMap, dirPath);
                dirNode.add(new DefaultMutableTreeNode(new FileEntry(fileName, p, f.status)));
            } else {
                treeRoot.add(new DefaultMutableTreeNode(new FileEntry(p, p, f.status)));
            }
        }

        // Compact intermediate single-child directories
        compactTree(treeRoot);

        treeModel.reload();
        expandAll(filesTree, true);
    }

    private DefaultMutableTreeNode getOrCreateDirNode(
        DefaultMutableTreeNode root,
        Map<String, DefaultMutableTreeNode> dirMap,
        String dirPath
    ) {
        if (dirMap.containsKey(dirPath)) return dirMap.get(dirPath);

        int lastSlash = dirPath.lastIndexOf('/');
        DefaultMutableTreeNode parent;
        String dirName;

        if (lastSlash >= 0) {
            String parentPath = dirPath.substring(0, lastSlash);
            dirName = dirPath.substring(lastSlash + 1);
            parent = getOrCreateDirNode(root, dirMap, parentPath);
        } else {
            dirName = dirPath;
            parent = root;
        }

        DefaultMutableTreeNode node = new DefaultMutableTreeNode(new DirEntry(dirName, dirName, 0));
        parent.add(node);
        dirMap.put(dirPath, node);
        return node;
    }

    private int compactTree(DefaultMutableTreeNode node) {
        int fileCount = 0;
        List<DefaultMutableTreeNode> children = new ArrayList<>();
        for (int i = 0; i < node.getChildCount(); i++) {
            children.add((DefaultMutableTreeNode) node.getChildAt(i));
        }

        for (DefaultMutableTreeNode child : children) {
            if (child.getUserObject() instanceof DirEntry) {
                fileCount += compactTree(child);
            } else {
                fileCount++;
            }
        }

        if (node.getUserObject() instanceof DirEntry de && node != treeRoot) {
            if (node.getChildCount() == 1 && node.getChildAt(0) instanceof DefaultMutableTreeNode singleChild && singleChild.getUserObject() instanceof DirEntry sub) {
                de.displayName = de.displayName + "/" + sub.displayName;
                node.removeAllChildren();
                for (int i = 0; i < singleChild.getChildCount(); i++) {
                    node.add((DefaultMutableTreeNode) singleChild.getChildAt(i));
                }
            }
            de.fileCount = fileCount;
        }

        return fileCount;
    }

    private static void expandAll(JTree tree, boolean expand) {
        TreeNode root = (TreeNode) tree.getModel().getRoot();
        expandAll(tree, new TreePath(root), expand);
    }

    private static void expandAll(JTree tree, TreePath parent, boolean expand) {
        TreeNode node = (TreeNode) parent.getLastPathComponent();
        if (node.getChildCount() >= 0) {
            for (Enumeration<?> e = node.children(); e.hasMoreElements(); ) {
                TreeNode n = (TreeNode) e.nextElement();
                TreePath path = parent.pathByAddingChild(n);
                expandAll(tree, path, expand);
            }
        }
        if (expand) tree.expandPath(parent);
        else if (parent.getPathCount() > 1) tree.collapsePath(parent);
    }

    private static List<String> parseBranches(String refs) {
        List<String> list = new ArrayList<>();
        if (refs == null || refs.isBlank()) return list;

        String clean = refs.trim();
        if (clean.startsWith("(") && clean.endsWith(")")) {
            clean = clean.substring(1, clean.length() - 1);
        }

        for (String part : clean.split(",")) {
            String p = part.trim();
            if (p.isEmpty()) continue;
            if (p.startsWith("HEAD -> ")) {
                list.add(p.substring(8).trim());
            } else if (!p.equals("HEAD")) {
                list.add(p);
            }
        }
        return list;
    }

    private void popup(MouseEvent e) {
        if (!e.isPopupTrigger()) return;
        int row = commitTable.rowAtPoint(e.getPoint());
        if (row >= 0) commitTable.setRowSelectionInterval(row, row);
        if (selectedCommit == null) return;

        JPopupMenu menu = new JPopupMenu();

        if (selectedCommit.isWorkingTree()) {
            JLabel header = new JLabel("  Working tree (" + uncommittedChanges.size() + " files)");
            header.setFont(AtomTheme.UI.deriveFont(Font.BOLD, 12f));
            header.setForeground(AtomTheme.GOLD);
            menu.add(header);
            menu.addSeparator();

            JMenuItem commit = new JMenuItem("Commit Staged Changes...", Icons.get("check", AtomTheme.BLUE, 15));
            commit.addActionListener(ev -> callbacks.commitWorkingTree());
            menu.add(commit);

            JMenuItem stash = new JMenuItem("Save Edits to Stash...", Icons.get("shelf", AtomTheme.SECONDARY, 15));
            stash.addActionListener(ev -> callbacks.stashWorkingTree());
            menu.add(stash);

            JMenuItem refresh = new JMenuItem("Refresh Working Tree", Icons.get("refresh", AtomTheme.SECONDARY, 15));
            refresh.addActionListener(ev -> update(git));
            menu.add(refresh);

            menu.show(commitTable, e.getX(), e.getY());
            return;
        }

        // WebStorm Commit Context Menu
        List<String> branches = parseBranches(selectedCommit.refs);

        // Branch submenus
        for (String branch : branches) {
            JMenu brMenu = new JMenu("Branch '" + branch + "'");
            brMenu.setFont(AtomTheme.UI.deriveFont(12f));
            brMenu.setForeground(AtomTheme.BRIGHT);
            brMenu.setIcon(Icons.get("branch", AtomTheme.BLUE, 15));

            JMenuItem checkout = new JMenuItem("Checkout", Icons.get("check", AtomTheme.BLUE, 14));
            checkout.addActionListener(ev -> callbacks.checkout(branch));
            brMenu.add(checkout);

            JMenuItem newBranchFrom = new JMenuItem("New Branch from '" + branch + "'...", Icons.get("plus", AtomTheme.SECONDARY, 14));
            newBranchFrom.addActionListener(ev -> callbacks.newBranch(branch));
            brMenu.add(newBranchFrom);

            JMenuItem checkoutRebase = new JMenuItem("Checkout and Rebase onto '" + currentBranch + "'", Icons.get("branch", AtomTheme.SECONDARY, 14));
            checkoutRebase.addActionListener(ev -> {
                callbacks.checkout(branch);
                callbacks.rebase(currentBranch);
            });
            brMenu.add(checkoutRebase);

            brMenu.addSeparator();

            JMenuItem compare = new JMenuItem("Compare with '" + currentBranch + "'", Icons.get("diff", AtomTheme.SECONDARY, 14));
            compare.addActionListener(ev -> callbacks.compare(branch));
            brMenu.add(compare);

            JMenuItem showDiff = new JMenuItem("Show Diff with Working Tree", Icons.get("diff", AtomTheme.SECONDARY, 14));
            showDiff.addActionListener(ev -> callbacks.showDiff(branch, ""));
            brMenu.add(showDiff);

            brMenu.addSeparator();

            JMenuItem rebaseOnto = new JMenuItem("Rebase '" + currentBranch + "' onto '" + branch + "'", Icons.get("branch", AtomTheme.SECONDARY, 14));
            rebaseOnto.addActionListener(ev -> callbacks.rebase(branch));
            brMenu.add(rebaseOnto);

            JMenuItem merge = new JMenuItem("Merge '" + branch + "' into '" + currentBranch + "'", Icons.get("merge", AtomTheme.SECONDARY, 14));
            merge.addActionListener(ev -> callbacks.merge(branch, false));
            brMenu.add(merge);

            JMenuItem newWt = new JMenuItem("New Worktree...", Icons.get("worktree", AtomTheme.GOLD, 14));
            newWt.addActionListener(ev -> callbacks.newWorktree(branch));
            brMenu.add(newWt);

            brMenu.addSeparator();

            JMenuItem pullRebase = new JMenuItem("Pull into '" + currentBranch + "' Using Rebase", Icons.get("pull", AtomTheme.SECONDARY, 14));
            pullRebase.addActionListener(ev -> callbacks.pull(branch, true));
            brMenu.add(pullRebase);

            JMenuItem pullMerge = new JMenuItem("Pull into '" + currentBranch + "' Using Merge", Icons.get("fetch", AtomTheme.BLUE, 14));
            pullMerge.addActionListener(ev -> callbacks.pull(branch, false));
            brMenu.add(pullMerge);

            brMenu.addSeparator();

            JMenuItem del = new JMenuItem("Delete", Icons.get("close", AtomTheme.RED, 14));
            del.addActionListener(ev -> callbacks.deleteBranch(branch));
            brMenu.add(del);

            menu.add(brMenu);
        }

        if (!branches.isEmpty()) menu.addSeparator();

        // Commit-level operations
        JMenuItem pushAll = new JMenuItem("Push All up to Here...", Icons.get("push", AtomTheme.SECONDARY, 15));
        pushAll.addActionListener(ev -> callbacks.pushUpTo(selectedCommit.hash));
        menu.add(pushAll);

        JMenuItem newBranch = new JMenuItem("New Branch...", Icons.get("plus", AtomTheme.BLUE, 15));
        newBranch.setAccelerator(KeyStroke.getKeyStroke(KeyEvent.VK_N, InputEvent.CTRL_DOWN_MASK | InputEvent.ALT_DOWN_MASK));
        newBranch.addActionListener(ev -> callbacks.newBranch(selectedCommit.hash));
        menu.add(newBranch);

        JMenuItem newTag = new JMenuItem("New Tag...", Icons.get("branch", AtomTheme.SECONDARY, 15));
        newTag.addActionListener(ev -> callbacks.newTag(selectedCommit.hash));
        menu.add(newTag);

        JMenuItem newWtFromCommit = new JMenuItem("New Worktree from this commit...", Icons.get("worktree", AtomTheme.GOLD, 15));
        newWtFromCommit.addActionListener(ev -> callbacks.newWorktree(selectedCommit.hash));
        menu.add(newWtFromCommit);

        menu.addSeparator();

        JMenuItem goChild = new JMenuItem("Go to Child Commit", Icons.get("chevron-right", AtomTheme.SECONDARY, 15));
        goChild.setAccelerator(KeyStroke.getKeyStroke(KeyEvent.VK_LEFT, 0));
        goChild.addActionListener(ev -> {
            int cur = commitTable.getSelectedRow();
            if (cur > 0) commitTable.setRowSelectionInterval(cur - 1, cur - 1);
        });
        menu.add(goChild);

        JMenuItem goParent = new JMenuItem("Go to Parent Commit", Icons.get("chevron-down", AtomTheme.SECONDARY, 15));
        goParent.setAccelerator(KeyStroke.getKeyStroke(KeyEvent.VK_RIGHT, 0));
        goParent.addActionListener(ev -> {
            int cur = commitTable.getSelectedRow();
            if (cur >= 0 && cur < commitTable.getRowCount() - 1) commitTable.setRowSelectionInterval(cur + 1, cur + 1);
        });
        menu.add(goParent);

        menu.addSeparator();

        JMenuItem copyHash = new JMenuItem("Copy Commit Hash", Icons.get("file", AtomTheme.SECONDARY, 15));
        copyHash.addActionListener(ev -> {
            Toolkit.getDefaultToolkit().getSystemClipboard().setContents(new StringSelection(selectedCommit.hash), null);
            callbacks.log("Copied commit hash to clipboard: " + selectedCommit.hash);
        });
        menu.add(copyHash);

        menu.show(commitTable, e.getX(), e.getY());
    }

    private static JLabel makeBranchPill(String name, Color bg, Color fg) {
        JLabel pill = new JLabel(" " + name + " ") {
            protected void paintComponent(Graphics g) {
                Graphics2D g2 = (Graphics2D) g.create();
                g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
                g2.setColor(bg);
                g2.fillRoundRect(0, 0, getWidth(), getHeight(), 8, 8);
                g2.dispose();
                super.paintComponent(g);
            }
        };
        pill.setFont(AtomTheme.CODE.deriveFont(Font.BOLD, 10.5f));
        pill.setForeground(fg);
        pill.setOpaque(false);
        pill.setBorder(new EmptyBorder(1, 4, 1, 4));
        return pill;
    }

    // Node classes for files tree
    public static final class DirEntry {
        public String fullPath;
        public String displayName;
        public int fileCount;

        public DirEntry(String fullPath, String displayName, int fileCount) {
            this.fullPath = fullPath;
            this.displayName = displayName;
            this.fileCount = fileCount;
        }

        @Override public String toString() {
            if (displayName.isEmpty() || displayName.equals(fileCount + " files") || displayName.equals(fileCount + " file")) {
                return fileCount + " " + (fileCount == 1 ? "file" : "files");
            }
            return displayName + (fileCount > 0 ? "  " + fileCount + " " + (fileCount == 1 ? "file" : "files") : "");
        }
    }

    public static final class FileEntry {
        public String name;
        public String path;
        public String status;

        public FileEntry(String name, String path, String status) {
            this.name = name;
            this.path = path;
            this.status = status;
        }

        @Override public String toString() { return name; }
    }

    private final class FileTreeCellRenderer extends JPanel implements TreeCellRenderer {
        private final JLabel iconLabel = new JLabel();
        private final JLabel nameLabel = new JLabel();
        private final JLabel countOrStatusLabel = new JLabel();

        FileTreeCellRenderer() {
            super(new BorderLayout(5, 0));
            setOpaque(true);
            setBorder(new EmptyBorder(1, 4, 1, 6));
            add(iconLabel, BorderLayout.WEST);
            add(nameLabel, BorderLayout.CENTER);
            add(countOrStatusLabel, BorderLayout.EAST);
        }

        public Component getTreeCellRendererComponent(
            JTree tree,
            Object value,
            boolean selected,
            boolean expanded,
            boolean leaf,
            int row,
            boolean hasFocus
        ) {
            setBackground(selected ? AtomTheme.SELECT : AtomTheme.HEADER);

            if (value instanceof DefaultMutableTreeNode n) {
                Object user = n.getUserObject();
                if (user instanceof DirEntry de) {
                    iconLabel.setIcon(Icons.get("folder", expanded ? AtomTheme.GOLD : AtomTheme.SECONDARY, 15));
                    nameLabel.setText(de.displayName);
                    nameLabel.setFont(AtomTheme.UI.deriveFont(Font.PLAIN, 12f));
                    nameLabel.setForeground(selected ? AtomTheme.BRIGHT : AtomTheme.TEXT);
                    countOrStatusLabel.setText(de.fileCount > 0 ? de.fileCount + " " + (de.fileCount == 1 ? "file" : "files") : "");
                    countOrStatusLabel.setFont(AtomTheme.UI.deriveFont(11f));
                    countOrStatusLabel.setForeground(selected ? AtomTheme.BRIGHT : AtomTheme.MUTED);
                    return this;
                } else if (user instanceof FileEntry fe) {
                    Color c = fe.status.contains("U") ? AtomTheme.RED :
                        fe.status.equals("??") || fe.status.contains("A") ? AtomTheme.GREEN :
                        fe.status.contains("D") ? AtomTheme.RED : AtomTheme.GOLD;

                    iconLabel.setIcon(Icons.get("file", c, 14));
                    nameLabel.setText(fe.name);
                    nameLabel.setFont(AtomTheme.UI.deriveFont(12f));
                    nameLabel.setForeground(selected ? AtomTheme.BRIGHT : AtomTheme.TEXT);
                    countOrStatusLabel.setText(fe.status);
                    countOrStatusLabel.setFont(AtomTheme.CODE.deriveFont(Font.BOLD, 11f));
                    countOrStatusLabel.setForeground(c);
                    return this;
                }
            }

            nameLabel.setText(value != null ? value.toString() : "");
            iconLabel.setIcon(null);
            countOrStatusLabel.setText("");
            return this;
        }
    }

    private final class FileTableCellRenderer extends DefaultTableCellRenderer {
        public Component getTableCellRendererComponent(JTable table, Object value, boolean isSelected, boolean hasFocus, int row, int column) {
            JLabel label = (JLabel) super.getTableCellRendererComponent(table, value, isSelected, hasFocus, row, column);
            label.setBorder(new EmptyBorder(2, 6, 2, 6));

            String status = (String) table.getValueAt(row, 0);
            Color c = status.contains("U") ? AtomTheme.RED :
                status.equals("??") || status.contains("A") ? AtomTheme.GREEN :
                status.contains("D") ? AtomTheme.RED : AtomTheme.GOLD;

            if (column == 0) {
                label.setText(status);
                label.setFont(AtomTheme.CODE.deriveFont(Font.BOLD, 11.5f));
                label.setForeground(isSelected ? AtomTheme.BRIGHT : c);
                label.setIcon(null);
            } else {
                label.setText(value != null ? value.toString() : "");
                label.setFont(AtomTheme.UI.deriveFont(12f));
                label.setForeground(isSelected ? AtomTheme.BRIGHT : AtomTheme.TEXT);
                label.setIcon(Icons.get("file", c, 14));
            }
            return label;
        }
    }

    private final class CommitCellRenderer extends DefaultTableCellRenderer {
        public Component getTableCellRendererComponent(JTable table, Object value, boolean isSelected, boolean hasFocus, int row, int column) {
            JLabel label = (JLabel) super.getTableCellRendererComponent(table, value, isSelected, hasFocus, row, column);
            label.setBorder(new EmptyBorder(2, 4, 2, 6));

            if (value instanceof Commit c) {
                if (c.isWorkingTree()) {
                    label.setText("<html><span style='color:#e5c07b;font-weight:bold;'>● Working tree</span> &nbsp; " + escapeHtml(c.subject) + "</html>");
                    label.setFont(AtomTheme.UI.deriveFont(Font.BOLD, 12f));
                    label.setForeground(isSelected ? AtomTheme.BRIGHT : AtomTheme.GOLD);
                    label.setIcon(new WorkingTreeDotIcon());
                    return label;
                }

                RowGraph g = commitGraphs.get(c);
                if (!c.refs.isEmpty()) {
                    String styledRefs = formatRefs(c.refs);
                    label.setText("<html>" + styledRefs + " " + escapeHtml(c.subject) + "</html>");
                } else {
                    label.setText(c.subject);
                }

                label.setFont(AtomTheme.UI.deriveFont(c.refs.contains("HEAD") ? Font.BOLD : Font.PLAIN, 12f));
                label.setForeground(isSelected ? AtomTheme.BRIGHT : AtomTheme.TEXT);
                label.setIcon(new CommitGraphDagIcon(g, isSelected));
            } else {
                label.setFont(AtomTheme.UI.deriveFont(12f));
                label.setForeground(isSelected ? AtomTheme.BRIGHT : column == 1 ? AtomTheme.SECONDARY : AtomTheme.MUTED);
                label.setIcon(null);
            }
            return label;
        }
    }

    private static final class WorkingTreeDotIcon implements Icon {
        public int getIconWidth() { return 24; }
        public int getIconHeight() { return 24; }
        public void paintIcon(Component c, Graphics g, int x, int y) {
            Graphics2D g2 = (Graphics2D) g.create();
            g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            g2.setColor(AtomTheme.GOLD);
            g2.draw(new Ellipse2D.Double(x + 6, y + 6, 11, 11));
            g2.fill(new Ellipse2D.Double(x + 9, y + 9, 5, 5));
            // Dashed connection downwards
            g2.setStroke(new BasicStroke(1.2f, BasicStroke.CAP_BUTT, BasicStroke.JOIN_BEVEL, 0, new float[]{2, 2}, 0));
            g2.setColor(AtomTheme.GOLD);
            g2.drawLine(x + 11, y + 17, x + 11, y + 24);
            g2.dispose();
        }
    }

    private static final class CommitGraphDagIcon implements Icon {
        private final RowGraph row;
        private final boolean isSelected;

        CommitGraphDagIcon(RowGraph row, boolean isSelected) {
            this.row = row;
            this.isSelected = isSelected;
        }

        public int getIconWidth() {
            int lanes = row != null ? Math.max(2, row.totalLanes) : 2;
            return Math.min(120, lanes * 16 + 10);
        }

        public int getIconHeight() { return 24; }

        public void paintIcon(Component c, Graphics g, int x, int y) {
            if (row == null) return;
            Graphics2D g2 = (Graphics2D) g.create();
            g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            g2.setStroke(new BasicStroke(1.8f));

            int laneSpacing = 16;
            int xOffset = x + 10;
            int yCenter = y + 12;

            // 1. Draw pass-through lines
            for (int l : row.passThrough) {
                int lx = xOffset + l * laneSpacing;
                g2.setColor(LANE_COLORS[l % LANE_COLORS.length]);
                g2.drawLine(lx, y, lx, y + 24);
            }

            // 2. Draw vertical commit lane line
            int cx = xOffset + row.commitLane * laneSpacing;
            g2.setColor(LANE_COLORS[row.commitLane % LANE_COLORS.length]);
            g2.drawLine(cx, y, cx, yCenter);
            g2.drawLine(cx, yCenter, cx, y + 24);

            // 3. Draw merge / branch curves
            for (Edge edge : row.mergeEdges) {
                int fx = xOffset + edge.fromLane * laneSpacing;
                int tx = xOffset + edge.toLane * laneSpacing;
                g2.setColor(LANE_COLORS[edge.toLane % LANE_COLORS.length]);
                Path2D.Double curve = new Path2D.Double();
                curve.moveTo(fx, yCenter);
                curve.curveTo(fx, yCenter + 10, tx, yCenter + 4, tx, y + 24);
                g2.draw(curve);
            }

            // 4. Draw commit node dot
            g2.setColor(LANE_COLORS[row.commitLane % LANE_COLORS.length]);
            if (row.isHead) {
                g2.draw(new Ellipse2D.Double(cx - 5.5, yCenter - 5.5, 11, 11));
                g2.fill(new Ellipse2D.Double(cx - 2.5, yCenter - 2.5, 5, 5));
            } else if (row.isMerge) {
                g2.fill(new Ellipse2D.Double(cx - 4.5, yCenter - 4.5, 9, 9));
                g2.setColor(Color.WHITE);
                g2.fill(new Ellipse2D.Double(cx - 2, yCenter - 2, 4, 4));
            } else {
                g2.fill(new Ellipse2D.Double(cx - 3.5, yCenter - 3.5, 7, 7));
            }

            g2.dispose();
        }
    }

    private static String formatRefs(String refs) {
        StringBuilder sb = new StringBuilder();
        List<String> list = parseBranches(refs);
        for (String ref : list) {
            boolean remote = ref.startsWith("origin/") || ref.startsWith("upstream/");
            String color = remote ? "#c678dd" : "#61afef";
            String bg = remote ? "#3d2247" : "#1a3556";
            sb.append("<span style='background-color:").append(bg).append("; color:").append(color)
              .append("; font-weight:bold; font-size:10px;'>&nbsp;").append(escapeHtml(ref)).append("&nbsp;</span> ");
        }
        return sb.toString();
    }

    private static String escapeHtml(String text) {
        if (text == null) return "";
        return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;");
    }
}
