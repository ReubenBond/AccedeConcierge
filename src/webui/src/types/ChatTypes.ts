export interface GitHubIssue {
	owner: string;
	repository: string;
	issueNumber: number;
}

export interface GitHubIssueWithStatus {
	id: GitHubIssue;
	title: string;
	type: string;
	status: string;
	labels: string[];
}

export interface Chat {
	id: string;
	issue: GitHubIssueWithStatus;
}

export interface Message {
	role: string;
	text: string;
	responseId: string;
	type: string;
	data?: string;
}

export interface MessageFragment extends Message {
	isFinal: boolean;
}
